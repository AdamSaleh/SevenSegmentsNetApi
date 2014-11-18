using System;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SevenSegmentsApi
{


	public enum BulkResult {Ok, Error, Retry};

	public abstract class Command {

		public static long Epoch(){
			var t0 = DateTime.UtcNow;
			var tEpoch = new DateTime (1970, 1, 1, 0, 0, 0);
			return (long)Math.Truncate (t0.Subtract (tEpoch).TotalSeconds);
		}

		public String Response;
		public BulkResult Result;
		public Task<Command> Task;
		public WebException WebException;
		public abstract String Endpoint {
			get ;
		}

		public abstract Object JsonPayload {
			get ;
		}


		public override String ToString () {
			String s = "curl -X \'POST\' https://api.7segments.com/"+Endpoint+"  -H \"Content-type: application/json\" -d \'" +
			           JsonConvert.SerializeObject (JsonPayload) + "\'";
			if (Response != null) {
				s += "\n\n" + Response;
			}
			return s;
		}
	}

	public class Customer : Command {
		private String Company;
		private Object CustomerIds;
		private Object Properties;
		public Customer (String company,Object customerId,Object properties)
		{
			Company = company;
			CustomerIds = customerId;
			Properties = properties;
		}


		public override String Endpoint {
			get { 
				return "crm/customers";
			}
		}

		public override Object JsonPayload {
			get { 
				if (Properties != null) {
					return new Dictionary<Object,Object>(){
						{"ids", CustomerIds },
						{"company_id",  Company}, 
						{"properties", Properties} 
					};
				} else {
					return new Dictionary<Object,Object>(){
						{"ids",  CustomerIds },
						{"company_id",  Company}, 
					};
				}
			}
		}
	}

	public class Event : Command {
		private String Type;
		private long Time;
		private String Company;
		private Object Customer;
		private Object Properties;

		public Event (String company,Object customer,String type,Object properties, long time)
		{
			Company = company;
			Customer = customer;
			Properties = properties;
			Type = type;
			Time = time;
		}

		public override String Endpoint {
			get { 
				return "crm/events";
			}
		}
		public override Object JsonPayload {
			get { 
				if (Properties != null) {
					return new Dictionary<Object,Object>(){
						{"customer_ids", Customer},
						{"company_id",  Company}, 
						{"properties", Properties}, 
						{"type",  Type}, 
						{"age",  Epoch () - this.Time}
					};
				} else {
					return  new Dictionary<Object,Object>(){
						{"customer_ids", Customer },
						{"company_id",  Company}, 
						{"type",  Type}, 
						{"age",  Epoch () - this.Time},
					};
				}
			}
		}
	}

	public class SevenSegments
	{
			
		protected static async Task<String> post(Uri url, string postdata)
		{
			var request = WebRequest.Create(url) as HttpWebRequest;
			request.Method = "POST";
			request.ContentType = "application/json";

			byte[] data = Encoding.UTF8.GetBytes(postdata);

			using (var requestStream = await Task<Stream>.Factory.FromAsync(request.BeginGetRequestStream, request.EndGetRequestStream, request))
			{
				await requestStream.WriteAsync(data, 0, data.Length);
			}
			WebResponse responseObject = await Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, request);
			var responseStream = responseObject.GetResponseStream();
			var sr = new StreamReader(responseStream);
			string received = await sr.ReadToEndAsync();

			return received;
		}
			

		protected readonly String CompanyToken;
		protected readonly Uri Target;
		protected Object Customer;

		public SevenSegments (String companyToken, Uri target, String customer) {
		    CompanyToken = companyToken;
			Target = target;
			Customer = customer;
		}

		public Task<Command> Update(Object properties){
			return ScheduleCustomer (CompanyToken, Customer, properties);
		}

		public Task<Command> Identify(Object customer,Object properties){
			Customer = customer;
			return ScheduleCustomer (CompanyToken, customer, properties);
		}

		protected Task<Command> ScheduleCustomer(String Company,Object Customer,Object Properties){
			return ScheduleCommand(new Customer( Company, Customer, Properties));
		}
	
		public Task<Command> Track(String Type,Object Properties, long Time){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, Properties, Time));
		}

		public Task<Command> Track(String Type, long Time){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, null, Time));
		}

	
		public Task<Command> Track(String Type){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, null, Command.Epoch()));
		}

		public Task<Command> Track(String Type,Object Properties){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, Properties, Command.Epoch()));
		}

		protected virtual async Task<Command> ScheduleCommand(Command command){
			try{
				string result = await post(new Uri(Target.ToString()+command.Endpoint),JsonConvert.SerializeObject(command.JsonPayload));
				command.Response = result;
				command.Result = BulkResult.Ok;
			}catch(System.Net.WebException e){
				command.Result = BulkResult.Error;
				command.WebException = e;
			}
			return command;
		}
			
	}

	public class SeventSegmentsBulkUpload : SevenSegments
	{
		protected static async Task<List<Command>> bulkUpload(Uri target, List<Command> bulk ){
			String payload = JsonConvert.SerializeObject( new Dictionary<String,Object>() {
				{"commands", (from item in bulk	select new Dictionary<String,Object>(){{"name",item.Endpoint},{"data",item.JsonPayload}}).ToList()}});
			String result = await post (new Uri(target.ToString()+"/bulk"),payload);

			return deserializeBulkManualy (bulk,result);
		}

		protected static List<Command> deserializeBulkManualy(List<Command> bulk, String input){
			JObject o = JObject.Parse (input);
			var l = o ["results"];
			if (l == null) {
				return bulk.Select ((Command arg) => {
					arg.Result = BulkResult.Error;
					arg.Response = input;
					return arg;
				}).ToList ();
			}

			return bulk.Zip(l,(Command c, JToken item) => {
				BulkResult tmpBulkResult = BulkResult.Error;
				var status = item ["status"];
				if (status != null) {
					if (status.Value<String> () == "ok") {
						tmpBulkResult = BulkResult.Ok;
					}
					if (status.Value<String> () == "retry") {
						tmpBulkResult = BulkResult.Retry;
					}
				}
				c.Response = item.ToString();
				c.Result = tmpBulkResult;
				return c;
			}).ToList();

		}


		public readonly ConcurrentQueue<Command> commands;
		private readonly ConcurrentQueue<List<Command>> bulkCommandsInProgress;
		private readonly ConcurrentQueue<Command> erroredCommands;
		private readonly ConcurrentQueue<Command> retryCommands;
		private readonly ConcurrentQueue<Command> successfullCommands;

		public SeventSegmentsBulkUpload (String companyToken, Uri target, String customer) : base(companyToken,target,customer) {
			commands = new ConcurrentQueue<Command>();
			erroredCommands = new ConcurrentQueue<Command>();
			retryCommands = new ConcurrentQueue<Command>();
			successfullCommands = new ConcurrentQueue<Command>();
		}
			

		public ConcurrentQueue<Command> ErroredCommands {
			get {
				return erroredCommands;
			}
		}
			
		public ConcurrentQueue<Command> SuccessfullCommands {
			get {
				return successfullCommands;
			}
		}


		protected override Task<Command> ScheduleCommand(Command command){
			command.Task = new Task<Command> (() => {
				return command;
			});

			commands.Enqueue(command);
			return command.Task;
		}

		private Command processFinishedCommand(Command item){
			if(item.Result == BulkResult.Ok){
				SuccessfullCommands.Enqueue (item);
			}else if(item.Result == BulkResult.Retry){
				retryCommands.Enqueue(item);
			}else {
				ErroredCommands.Enqueue(item);
			}
			item.Task.Start();
			return item;
		}

		private Command failCommand(Command item,WebException e){
			item.WebException = e;
			ErroredCommands.Enqueue (item);
			return item;
		}

		public void ScheduleRetry(){
			Command tmpCommand;
			while (retryCommands.TryDequeue (out tmpCommand)) {
				ScheduleCommand (tmpCommand);
			}
		}

		public async Task<SeventSegmentsBulkUpload> BulkUpload(){
			while (commands.Count > 0) {
				List<Command> tmpList = new List<Command> ();
				Command tmpCommand;
				while (tmpList.Count < 49 && commands.TryDequeue (out tmpCommand)) {
					tmpList.Add (tmpCommand);
				}
				
				if (tmpList.Count > 0) {
					try {
						var result = await bulkUpload (Target, tmpList);
						foreach(var cmd in result){
							processFinishedCommand (cmd);
						}
					} catch (System.Net.WebException e) {
						foreach(var cmd in tmpList){
							failCommand (cmd,e);
						}
					}
				}
				
			}
			return this;

		} 

	}

	public class SeventSegmentsAutomaticBulkUpload : SeventSegmentsBulkUpload
	{
		public readonly Task EventLoop;
		private Boolean Running;
		private Task<SeventSegmentsBulkUpload> Uploading;
		private Task Finishing = null;
		public SeventSegmentsAutomaticBulkUpload (String companyToken, Uri target, String customer,int delay) : base(companyToken,target,customer) {
			Running = true;
			EventLoop = new Task (delegate {
				while(Running){
					Uploading = this.BulkUpload();
					Uploading.Wait();
					if(Finishing!=null){
						Finishing.Start();
						return;
					}
					Task.Delay(delay).Wait();
				}
			});
			EventLoop.Start ();
		}

		public Task EndEventLoop(){
			Finishing = new Task (delegate {

			});
			return Finishing;
		}

	}

}