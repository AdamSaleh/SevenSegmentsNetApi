using System;
using System.Net;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using SevenSegmentsApi;
using System.Threading.Tasks;

namespace SevenSegmentsConsole
{


	class MainClass
	{

		class CustomerProperty {
			public String email;
			public String[] tags;

			public CustomerProperty (String email, String[] tags)
			{
				this.email = email;
				this.tags = tags;
			}
	
		}

		class EventProperty {
			public int numberOfClicks;

			public EventProperty (int numberOfClicks)
			{
				this.numberOfClicks = numberOfClicks;
			}

		}

		public static String company_token = "d5b474ce-61b8-11e4-8f55-0cc47a049482";

		public static void BasicTracking(){
			var customer1_id = Guid.NewGuid ().ToString ();

			var sevenSegments = new  SevenSegments (company_token,new Uri("https://api.7segments.com/"),customer1_id);

			sevenSegments.Identify (
				customer1_id,
				new Dictionary<String, Object> () {
					{"email","asdf@asdf.com"}}).Wait();
			sevenSegments.Track ("login").Wait();
			sevenSegments.Track ("dothing",new EventProperty(10)).Wait();
			sevenSegments.Track ("logout").Wait ();
		}

		public static void ManualBulkTracking(){
			var customer1_id = Guid.NewGuid ().ToString ();

			var sevenSegments = new  SeventSegmentsBulkUpload (company_token,new Uri("https://api.7segments.com/"),customer1_id);

			sevenSegments.Identify (
				new Dictionary<String, String> () {{"registered",customer1_id}},
				new Dictionary<String, Object> () {
					{"email","asdf@asdf.com"}}).Wait();
			sevenSegments.Track ("login").Wait();
			sevenSegments.Track ("dothing",new EventProperty(10)).Wait();
			sevenSegments.Track ("logout").Wait ();

			sevenSegments.BulkUpload ().Wait();
		}
			
		public static void AutomaticBulkTracking(){
			var customer1_id = Guid.NewGuid ().ToString ();

			var sevenSegments = new  SeventSegmentsAutomaticBulkUpload (company_token,new Uri("https://api.7segments.com/"),customer1_id,1000);

			sevenSegments.Identify (
				new Dictionary<String, String> () {{"registered",customer1_id}},
				new Dictionary<String, Object> () {
					{"email","asdf@asdf.com"}});
			sevenSegments.Track ("login");
			sevenSegments.Track ("dothing",new EventProperty(10));
			sevenSegments.Track ("logout");

			sevenSegments.EndEventLoop ().Wait();
		}

		public static void Main (string[] args)
		{	
			BasicTracking ();
			ManualBulkTracking ();
			AutomaticBulkTracking ();
		}
	

	
	}
}
