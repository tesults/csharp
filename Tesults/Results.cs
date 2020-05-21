using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Amazon.S3.Transfer;

namespace Tesults
{
    public class Results
    {
        private class RefreshCredentialsResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public Dictionary<string, object> Upload { get; set; }
        }

        private class FilesUploadReturn
        {
            public string Message { get; set; }
            public List<string> Warnings { get; set; }
        }

        private class ResultsFile
        {
            public int Num { get; set; }
            public string File { get; set; }
        }

        private static RefreshCredentialsResponse RefreshCredentialsError (string message)
        {
            var response = new RefreshCredentialsResponse();
            response.Success = false;
            response.Message = message;
            response.Upload = new Dictionary<string, object>();
            return response;
        }

        private static RefreshCredentialsResponse RefreshCredentials(String target, String key)
        {
            var data = new Dictionary<string, string>();
            data.Add("target", target);
            data.Add("key", key);


            string json = JsonConvert.SerializeObject(data);
            var client = new HttpClient();
            var uri = new Uri(@"https://www.tesults.com/permitupload");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response = null;
            try
            {
                var responseTask = client.PostAsync(uri, content);
                response = responseTask.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (response == null)
            {
                return RefreshCredentialsError("Error refreshing credentials.");
            }

            var responseString = response.Content.ReadAsStringAsync().Result;
            dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);

            if (responseJson.data != null)
            {
                // success
                var credentials = new RefreshCredentialsResponse();
                credentials.Success = true;
                credentials.Message = Convert.ToString(responseJson.data.message);
                

                var upload = responseJson.data.upload;
                string k = Convert.ToString(upload.key);
                string uploadMessage = Convert.ToString(upload.message);
                bool permit = Convert.ToBoolean(upload.permit);
                var auth = new Dictionary<string, string>();
                auth.Add("AccessKeyId", Convert.ToString(upload.auth.AccessKeyId));
                auth.Add("SecretAccessKey", Convert.ToString(upload.auth.SecretAccessKey));
                auth.Add("SessionToken", Convert.ToString(upload.auth.SessionToken));
                auth.Add("Expiration", Convert.ToString(upload.auth.Expiration));

                credentials.Upload = new Dictionary<string, object>();
                credentials.Upload.Add("key", k);
                credentials.Upload.Add("message", uploadMessage);
                credentials.Upload.Add("permit", permit);
                credentials.Upload.Add("auth", auth);

                return credentials;
            }
            else
            {
                // failure
                return RefreshCredentialsError(Convert.ToString(responseJson.error.message));
            }
        }

        private static TransferUtility CreateTransferUtility(Dictionary<string, string> auth)
        {
            
            string accessKeyId = (string)auth["AccessKeyId"];
            string secretAccessKey = (string)auth["SecretAccessKey"];
            string sessionToken = (string)auth["SessionToken"];

            var s3Client = new Amazon.S3.AmazonS3Client(accessKeyId, secretAccessKey, sessionToken, Amazon.RegionEndpoint.USEast1);

            return new TransferUtility(s3Client);
        }

        static int filesUploaded = 0;
        static long bytesUploaded = 0;
        static List<string> warnings = new List<string>();
        static List<string> uploading = new List<string>();

        private static void TransferUploadProgressEvent(object sender, UploadProgressArgs e)
        {
            if (e.TransferredBytes == e.TotalBytes)
            {
                bytesUploaded += e.TotalBytes;
                filesUploaded++;

                //var file = e.FilePath;
                uploading.Remove(e.FilePath);
            }
        }

        private static FilesUploadReturn FilesUpload (List<ResultsFile> files, string keyPrefix, Dictionary<string, string> auth, string target)
        {
            
            const long expireBuffer = 30; // 30 seconds
            var expiration = Convert.ToInt64((string) auth["Expiration"]);
            
            const int maxActiveUploads = 10; // Upload at most 10 files simultaneously to avoid hogging the client machine.
            var transferUtility = CreateTransferUtility(auth);

            while (files.Count != 0 || uploading.Count != 0)
            {
                try
                {
                    if (uploading.Count < maxActiveUploads && files.Count != 0)
                    {
                        // Check if new credentials required.
                        long now = (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000000;
                        if (now + expireBuffer > expiration) // Check within 30 seconds of expiry.
                        {
                            if (uploading.Count == 0)
                            {
                                // Wait for all current transfers to complete before creating new TransferUtility.

                                RefreshCredentialsResponse response = RefreshCredentials(target, keyPrefix);
                                if (response.Success != true)
                                {
                                    // Must stop upload due to failure to get new credentials.
                                    warnings.Add(response.Message);
                                    break;
                                }
                                else
                                {
                                    string key = (string)response.Upload["key"];
                                    string uploadMessage = (string)response.Upload["message"];
                                    bool permit = (bool)response.Upload["permit"];
                                    auth = (Dictionary<string, string>)response.Upload["auth"];

                                    if (permit != true)
                                    {
                                        // Must stop upload due to failure to be permitted for new credentials.
                                        warnings.Add(uploadMessage);
                                        break;
                                    }

                                    // upload permitted
                                    expiration = Convert.ToInt64((string)auth["Expiration"]);
                                    transferUtility = CreateTransferUtility(auth);
                                }
                            }
                        }
                        
                        if (now + expireBuffer < expiration)
                        { // Check within 30 seconds of expiry.
                            // Load new file for upload.
                            if (files.Count > 0)
                            {
                                ResultsFile resultsFile = files[0];
                                files.RemoveAt(0);
                                if (!File.Exists(resultsFile.File))
                                {
                                    warnings.Add("File not found: " + Path.GetFileName(resultsFile.File));
                                }
                                else
                                {
                                    String key = keyPrefix + "/" + resultsFile.Num + "/" + Path.GetFileName(resultsFile.File);
                                    var transfer = new TransferUtilityUploadRequest()
                                    {
                                        BucketName = "tesults-results",
                                        Key = key,
                                        FilePath = resultsFile.File 
                                    };
                                    transfer.UploadProgressEvent += TransferUploadProgressEvent;
                                    uploading.Add(resultsFile.File);
                                    try
                                    {
                                        transferUtility.Upload(transfer);
                                    }
                                    catch (Exception ex)
                                    {
                                        uploading.Remove(resultsFile.File);
                                        warnings.Add("Failed to upload file.");
                                    }
                                    
                                }
                            }   
                        }
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add("Error occurred while uploading files.");
                    break;
                }
            }

            return new FilesUploadReturn()
            {
                Message = "Success. " + filesUploaded + " files uploaded. " + bytesUploaded + " bytes uploaded.",
                Warnings = warnings
            };
        }

        private static List<ResultsFile> FilesInTestCases(Dictionary<string, object> data)
        {
            Dictionary<string, object> results = (Dictionary<string, object>) data["results"];
            List<Dictionary<string, object>> cases = (List < Dictionary<string, object>>) results["cases"];
            var files = new List<ResultsFile>();
            int num = 0;

            foreach (Dictionary<string, object> c in cases)
            {
                if (c.ContainsKey("files"))
                {
                    List<string> caseFiles = (List<string>) c["files"];
                    foreach (string caseFile in caseFiles)
                    {
                        var resultsFile = new ResultsFile() { Num = num, File = caseFile};
                        files.Add(resultsFile);
                    }
                }
                num++;
            }

            return files;
        }

        private static Dictionary<string, object> UploadResult (bool success, string message, List<string> warnings)
        {
            var uploadResult = new Dictionary<string, object>();
            var errors = new List<string>();
            uploadResult.Add("success", success);
            uploadResult.Add("message", message);
            if (success != true)
            {
                errors.Add(message);
            }
            uploadResult.Add("warnings", warnings);
            uploadResult.Add("errors", errors);
            return uploadResult;
        }

        public static Dictionary<string, object> Upload(Dictionary<string, object> data)
        {
            string json = JsonConvert.SerializeObject(data);
            var client = new HttpClient();
            var uri = new Uri(@"https://www.tesults.com/results");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage response = null;
            try
            {
                var responseTask = client.PostAsync(uri, content);
                response = responseTask.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            if (response == null)
            {
                return UploadResult(false, "Error uploading, check network.", new List<string>());
            }

            var responseString = response.Content.ReadAsStringAsync().Result;
            dynamic responseJson = JsonConvert.DeserializeObject<dynamic>(responseString);

            if (responseJson.data != null)
            {
                // success
                string message = Convert.ToString(responseJson.data.message);
                
                if (responseJson.data.upload != null)
                {
                    // upload required
                    var target = (string) data["target"];
                    var files = FilesInTestCases(data);

                    var upload = responseJson.data.upload;
                    string key = Convert.ToString(upload.key);
                    string uploadMessage = Convert.ToString(upload.message);
                    bool permit = Convert.ToBoolean(upload.permit);
                    var auth = new Dictionary<string, string>();
                    auth.Add("AccessKeyId", Convert.ToString(upload.auth.AccessKeyId));
                    auth.Add("SecretAccessKey", Convert.ToString(upload.auth.SecretAccessKey));
                    auth.Add("SessionToken", Convert.ToString(upload.auth.SessionToken));
                    auth.Add("Expiration", Convert.ToString(upload.auth.Expiration));

                    if (permit != true)
                    {
                        warnings.Add(uploadMessage);
                        return UploadResult(true, message, warnings);
                    }

                    // upload required and permitted
                    var fileUploadReturn = FilesUpload(files, key, auth, target); // this may take a while
                    return UploadResult(true, fileUploadReturn.Message, fileUploadReturn.Warnings);
                }
                else
                {
                    // upload not required
                    return UploadResult(true, message, warnings);
                }
            }
            else
            {
                // failure
                string message = Convert.ToString(responseJson.error.message);
                return UploadResult(false, message, warnings);
            }
        }
    }
}
