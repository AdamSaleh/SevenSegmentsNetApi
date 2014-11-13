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
			String company_id = "{EXAMPLE_COMPANY_ID}";
			var customer1_id = Guid.NewGuid ().ToString ();
			var customer2_id = Guid.NewGuid ().ToString ();

			var eventManager = new  EventManager ();

			eventManager.SetRetryOnException (async exc => {
				if (exc.Status == WebExceptionStatus.ConnectFailure) {
					return true;
				}
				return false;
			});

			eventManager.ScheduleCustomer (company_id, customer1_id, null)
				.ScheduleEvent (company_id, customer1_id, "login", null)
				.ScheduleEvent (company_id, customer1_id, "dothing", null)
				.ScheduleEvent (company_id, customer1_id, "logout", null)
				.ScheduleCustomer (company_id, customer2_id, null)
				.ScheduleEvent (company_id, customer2_id, "login", null)
				.ScheduleEvent (company_id, customer2_id, "dothat", null)
				.ScheduleEvent (company_id, customer2_id, "logout", null);

			var uploadTask = eventManager.BulkUpload ();

			eventManager.ScheduleEvent (company_id, customer1_id, "relogin", null);

			uploadTask.ContinueWith<EventManager> ((Task<EventManager> upload) => {
				var manager = upload.Result;
				manager.BulkUpload().Wait();
				return manager;
			});

			Command v;
			Console.WriteLine ("SUCCESS:");
			while (eventManager.SuccessfullCommands.TryDequeue (out v)) {
				Console.WriteLine (v.JsonPayload);
			}
			Console.WriteLine ("Fail:");

			while (eventManager.RetryCommands.TryDequeue (out v)) {
				Console.WriteLine (v.JsonPayload);
			}
			// Set the 'Method' property of the 'Webrequest' to 'POST'.
		
		}
	

	
	}
}
