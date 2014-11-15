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
			String company_id = "{COMPANY-ID}"
			var customer1_id = Guid.NewGuid ().ToString ();
			var customer2_id = Guid.NewGuid ().ToString ();



			var eventManager = new  EventManager (company_id,new Uri("https://api.7segments.com/"),customer1_id);

			eventManager.SetRetryOnException (exc => {
				if (exc.Status == WebExceptionStatus.ConnectFailure) {
					return true;
				}
				return false;
			});

			eventManager.Identify (customer1_id,new Dictionary<String, String> () {{"email","asdf@asdf.com"},{"tag","mfp"}});//,new CustomerProperty("a@b.com", new String[] {"foo","bar"}));
			eventManager.Track ("login");
			eventManager.Track ("dothing",new EventProperty(10));
			eventManager.Track ("logout");


			while (eventManager.AreTasksEnqueued || eventManager.BulkInProgress) {
				Console.WriteLine ("Waiting for tasks to be processed");
				Thread.Sleep(500);
			}

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
