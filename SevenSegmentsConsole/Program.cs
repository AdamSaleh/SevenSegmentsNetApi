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

		public static void Main (string[] args)
		{	
			String company_id = "d5b474ce-61b8-11e4-8f55-0cc47a049482";
			var customer1_id = Guid.NewGuid ().ToString ();
			var customer2_id = Guid.NewGuid ().ToString ();



			var eventManager = new  SeventSegmentsAutomaticBulkUpload (company_id,new Uri("https://api.7segments.com/"),customer1_id,1000);

			eventManager.Identify (
				new Dictionary<String, String> () {{"registered",customer1_id}},
				new Dictionary<String, Object> () {
					{"email","asdf@asdf.com"}});
			Task<Command> waitForLogin = eventManager.Track ("login");
			eventManager.Track ("dothing",new EventProperty(10));
			eventManager.Track ("logout");

			eventManager.EndEventLoop ().Wait();

			Command v;
			Console.WriteLine ("SUCCESS:");
			while (eventManager.SuccessfullCommands.TryDequeue (out v)) {
				Console.WriteLine (v);
			}
			Console.WriteLine ("Failed:");
			Command vv;
			while (eventManager.ErroredCommands.TryDequeue (out vv)) {
				Console.WriteLine (vv);
			}

		}
	

	
	}
}
