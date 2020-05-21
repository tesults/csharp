--------------------------------------------------------------
Tesults
--------------------------------------------------------------

Tesults is a test automation results reporting service. https://www.tesults.com

Tesults API library for uploading test results to Tesults in your C# application.

---------------------
Documentation
---------------------

Documentation is available at https://www.tesults.com/docs

---------------------
API Overview
---------------------

Upload your test results using the Tesults.Results.Upload method:

            Tesults.Results.Upload(data);

Here, the results data argument is of type: Dictionary<string,object>.

The data format should match https://www.tesults.com/docs?doc=resultsapi.

            // Required:
            // using System;
            // using System.Collections.Generic;

            // Create a list to hold your test case results.
            var testCases = new List<Dictionary<string, string>>();

            // Each test case is a dictionary. Usually you would
            // create these in a loop from whatever data objects your 
            // test framework provides.
            var testCase1 = new Dictionary<string, string>();
            testCase1.Add("name", "Test 1");
            testCase1.Add("desc", "Test 1 Description");
            testCase1.Add("suite", "Suite A");
            testCase1.Add("result", "pass");

            testCases.Add(testCase1);

            var testCase2 = new Dictionary<string, string>();
            testCase2.Add("name", "Test 2");
            testCase2.Add("desc", "Test 2 Description");
            testCase2.Add("suite", "Suite A");
            testCase2.Add("result", "pass");
	    testCase2.Add("files", new List<string>() { "/full/path/to/file/log.txt"});

            testCases.Add(testCase2);

            var testCase3 = new Dictionary<string, string>();
            testCase3.Add("name", "Test 3");
            testCase3.Add("desc", "Test 3 Description");
            testCase3.Add("suite", "Suite B");
            testCase3.Add("result", "fail");
            testCase3.Add("reason", "Assert fail in line 203 of example.cs");

            testCases.Add(testCase3);

            // The results dictionary will contain the test cases.
            var results = new Dictionary<string, object>();
            results.Add("cases", testCases);

            // Finally a dictionary to contain all of your results data.
            var data = new Dictionary<string, object>();
            data.Add("target", "token");
            data.Add("results", results);

            // Complete the upload.
            var response = Tesults.Results.Upload(data);

            // The response value is a dictionary with four keys.

	    // Value of key "success" is a bool, true if successfully uploaded, false otherwise.
            Console.WriteLine(res["success"]);

            // Value of key "message" is a string, useful to check if the upload was unsuccessful.
            Console.WriteLine(res["message"]);

	    // Value of key "warnings" is List<string>, if non empty there may be issues with files upload.
	    Console.WriteLine(((List<string>)res["warnings"]).Count)

	    // Value of key "errors" is List<string>, if success is true this will be empty.
	    Console.WriteLine(((List<string>)res["errors"]).Count)

The target value "token" above should you be replaced with your Tesults target token.
If you have lost your token you can recreate one at https://www.tesults.com/config.

The API library makes use of generic dictionaries rather than providing helper classes
so that the package does not require updating often as the Tesults service adds fields.

---------------------
Support
---------------------

support@tesults.com