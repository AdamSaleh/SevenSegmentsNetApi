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

		public static void Main (string[] args)
		{	
			String company_id = "{COMPANY_TOKEN}"
			var customer1_id = Guid.NewGuid ().ToString ();
			var customer2_id = Guid.NewGuid ().ToString ();



			var eventManager = new  EventManager (company_id,new Uri("https://api.7segments.com/bulk"),customer1_id);

			eventManager.SetRetryOnException (async exc => {
				if (exc.Status == WebExceptionStatus.ConnectFailure) {
					return true;
				}
				return false;
			});

			eventManager.Identify (customer1_id,new Dictionary<string, string> () { { "email","asdf@asdf.com" } });
			eventManager.Track ("login");
			eventManager.Track ("dothing");
			eventManager.Track ("logout");


			while (eventManager.AreTasksEnqueued || eventManager.BulkInProgress) {
				Console.WriteLine ("Waiting for tasks to be processed");
				Thread.Sleep(500);
			}

			Command v;
			Console.WriteLine ("SUCCESS:");
			while (eventManager.SuccessfullCommands.TryDequeue (out v)) {
				Console.WriteLine (v.JsonPayload);
			}
			Console.WriteLine ("Failed:");
			Command vv;
			while (eventManager.ErroredCommands.TryDequeue (out vv)) {
				Console.WriteLine (vv.JsonPayload);
			}

		}
	

	
	}
}
