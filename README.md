# SevenSegmentsApi

Implementation of json tracking api for [7segments](http://7segments.com). 

## Installation
 
It is a standard sln project. The SevenSegmentsApi subproject itself is a PCL library.
There is an example console application showcasing some examples of usage.

After you clone/checkout the project, you should be able to link it to your project from your C# IDE of choice. 

We are working on a nuget package.


## Usage

### Basic Interface

To start tracking, you need to know the uri of your 7segments api instance, usualy ```https://api.7segments.com/```, your ```company_token``` and generate a unique ```customer_id``` for the customer you are about to track. The unique ```customer_id``` can either be string, or an object representing the ```customer_ids``` as referenced in [the api guide](https://docs.7segments.com/technical-guide/integration-rest-client-api/#Detailed_key_descriptions).
Setting ```customer_id = "123-asdf"``` is equivalent to ```customer_id = new Dictionary<String, String> () {{"registered","123-adf"}};```


```
var sevenSegments = new  SevenSegments (company_token,server_uri,customer_id);
```

If this is the beginning of tracking of a new user, you need to first ```Identify``` him against the server.
Be warned, that this command is not thread-safe, so precautins should be taken, that identification sucessfully finished,
before issuing different commands. 

```
sevenSegments.Identify (customer_id, new Dictionary<String, Object> () {{"email","asdf@asdf.com"}}).Wait();
```

For serialization we utilize excelent [Json.Net][http://james.newtonking.com/json] library. This allows you to pass (almost) arbitrary
objects to be serialized as customer/event properties. 

```
class CustomerProperty {
	public String email;
	public String tag;

	public CustomerProperty (String email, tag)
	{
		this.email = email;
		this.tag = tag;
	}
}
```

```
sevenSegments.Identify (customer_id, new CustomerProperty("email","asdf@asdf.com"));
```

If update of the customer's properties is required.

```
sevenSegments.Update (customer_id, new CustomerProperty("email","jklo@jklo.com"));
```

You track various events by utilizing the Track function.
It assumes you want to track the customer you identified by ```Identify```.
The only required field is a string describing the type of your event.
By default the time of the event is tracked as the epoch time,
and diffed against the server time of 7segments before sending.

```
sevenSegments.Track ("login");
```

By utilizing Json.Net library, you can supply event properties as an arbitrary object, or
standard C# dictionary.

```
sevenSegments.Track ("event",new EventProperties(...));
```

All the methods are asynchronous and return a Command object that represents the ```POST``` request and response sent to the server.

```
sevenSegments.Track ("event",new EventProperties(...)).continueWith(cmd =>{
   Console.Writeln(cmd);
});
```

```ToString()``` method of ```Command``` returns a string description of a semanticaly equivalent curl command.

```
curl -X 'POST' -H 'Content-type: application/json' https://api.7segments.com/crm/events -d '{"type":"event","customer_ids":{"registered":"651c162b-c93d-4588-b4ef-5014638fecae"}, "company_id": "{COMPANY_TOKEN}", "preferences":{"email":"b@e.e", "zoo":{"zebra":5,"moose":10}} }'
{
  "errors": [],
  "success": true
}
```
### Bulk Upload
When using ```SeventSegmentsBulkUpload``` the asynchronous methods do not begin automatically, and just schedule requests
to an asynchronous queue.

```
var sevenSegments = new  SeventSegmentsBulkUpload (company_token,new Uri("https://api.7segments.com/"),customer1_id);
```
To send all of the scheduled commands on server calling of the asynchronous ```BulkUpload``` method is required.
```BulkUpload``` is able to send up to 50 events per request, making it more efficient than sending each event individually.

```
sevenSegments.BulkUpload ()
```
### Automatic Bulk Upload
Last implementation runs the Bulk upload periodically in a separate thread, creating a simple event loop, that uploads requests in bulk automatically. The last parameter in constructor specifies, how often the loop should wait after each upload in milliseconds. 
```
var sevenSegments = new  SeventSegmentsAutomaticBulkUpload (company_token,new Uri("https://api.7segments.com/"),customer1_id,1000);
```
You can end the automatic uploading by waiting on ```EndEventLoop``` metho, that first tries to finish all the queued events,
and then ends the thread the loop was running on.

```
sevenSegments.EndEventLoop ().Wait();
```