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

		public String Company;
		public String Customer;
		public Object Properties;

		public String Response;
		public BulkResult Result;

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
		public Customer (String Company,String Customer,Object Properties)
		{
			this.Company = Company;
			this.Customer = Customer;
			this.Properties = Properties;
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
						{"ids", new Dictionary<String,String>() {{"registered", Customer }}},
						{"company_id",  Company}, 
						{"properties", Properties} 
					};
				} else {
					return new Dictionary<Object,Object>(){
						{"idss", new Dictionary<String,String>() {{"registered", Customer }}},
						{"company_id",  Company}, 
					};
				}
			}
		}
	}

	public class Event : Command {
		public String Type;
		public long time;
		public long age;

		public Event (String Company,String Customer,String Type,Object Properties, long time)
		{
			this.Company = Company;
			this.Customer = Customer;
			this.Properties = Properties;
			this.Type = Type;
			this.time = time;
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
						{"customer_ids", new Dictionary<String,String>() {{"registered", Customer }}},
						{"company_id",  Company}, 
						{"properties", Properties}, 
						{"type",  Type}, 
						{"age",  this.age}
					};
				} else {
					return  new Dictionary<Object,Object>(){
						{"customer_ids", new Dictionary<String,String>() {{"registered", Customer }}},
						{"company_id",  Company}, 
						{"type",  Type}, 
						{"age",  this.age},
					};
				}
			}
		}
	}

	public class EventManager
	{
		private static long Epoch(){
			var t0 = DateTime.UtcNow;
			var tEpoch = new DateTime (1970, 1, 1, 0, 0, 0);
			return (long)Math.Truncate (t0.Subtract (tEpoch).TotalSeconds);
		}


		private static async Task<List<Command>> bulkUpload(Uri target, List<Command> bulk ){
			long currentTime = Epoch ();

			foreach (var item in bulk){
				if (item is Event) {
					var c = (Event)item;
					c.age = currentTime - c.time;
				}
			}
			String payload = JsonConvert.SerializeObject( new Dictionary<String,Object>() {
				{"commands", (from item in bulk	select new Dictionary<String,Object>(){{"name",item.Endpoint},{"data",item.JsonPayload}}).ToList()}});
			String result = await post (new Uri(target.ToString()+"/bulk"),payload);

			return deserializeBulkManualy (bulk,result);
		}

		private static List<Command> deserializeBulkManualy(List<Command> bulk, String input){
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

		private static async Task<String> post(Uri url, string postdata)
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


		private readonly ConcurrentQueue<Command> commands;
		private readonly ConcurrentQueue<List<Command>> bulkCommandsInProgress;
		private readonly ConcurrentQueue<Command> erroredCommands;
		private readonly ConcurrentQueue<Command> retryCommands;
		private readonly ConcurrentQueue<Command> successfullCommands;

		private readonly Boolean AutomaticUpload;

		private readonly String CompanyToken;
		private readonly Uri Target;
		private String Customer;
		private static long previousEpoch=0;

		public EventManager (String companyToken, Uri target, String customer) {
			commands = new ConcurrentQueue<Command> ();
			erroredCommands = new ConcurrentQueue<Command> ();
			retryCommands = new ConcurrentQueue<Command> ();
			bulkCommandsInProgress = new ConcurrentQueue<List<Command>> ();

			successfullCommands = new ConcurrentQueue<Command> ();
			exceptionEvaluator = (x) => {return false;};
			CompanyToken = companyToken;
			Target = target;
			Customer = customer;
			AutomaticUpload = true;
		}

		public EventManager (String companyToken, Uri target, String customer, Boolean automaticUpload) {
			commands = new ConcurrentQueue<Command> ();
			erroredCommands = new ConcurrentQueue<Command> ();
			retryCommands = new ConcurrentQueue<Command> ();
			successfullCommands = new ConcurrentQueue<Command> ();
			bulkCommandsInProgress = new ConcurrentQueue<List<Command>> ();
			exceptionEvaluator = (x) => {return false;};
			CompanyToken = companyToken;
			Target = target;
			Customer = customer;
			AutomaticUpload = automaticUpload;
		}

		public Task<EventManager> Update(Object properties){
			return ScheduleCustomer (CompanyToken, Customer, properties);
		}

		public Task<EventManager> Identify(String customer,Object properties){
			Customer = customer;
			return ScheduleCustomer (CompanyToken, customer, properties);
		}

		public ConcurrentQueue<Command> Commands {
			get {
				return commands;
			}
		}

		public ConcurrentQueue<Command> ErroredCommands {
			get {
				return erroredCommands;
			}
		}

		public ConcurrentQueue<Command> RetryCommands {
			get {
				return retryCommands;
			}
		}

		public ConcurrentQueue<Command> SuccessfullCommands {
			get {
				return successfullCommands;
			}
		}

		private Func<WebException,Boolean> exceptionEvaluator;
		public EventManager SetRetryOnException(Func<WebException,Boolean> evaluator){
			exceptionEvaluator = evaluator;
			return this;
		}

		private Task<EventManager> ScheduleCustomer(String Company,String Customer,Object Properties){
			return ScheduleCommand(new Customer( Company, Customer, Properties));
		}
	
		public Task<EventManager> Track(String Type,Object Properties, long Time){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, Properties, Time));
		}

		public Task<EventManager> Track(String Type, long Time){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, null, Time));
		}

	
		public Task<EventManager> Track(String Type){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, null, Epoch()));
		}

		public Task<EventManager> Track(String Type,Object Properties){
			return ScheduleCommand(new Event( CompanyToken, Customer, Type, Properties, Epoch()));
		}

		private Task<EventManager> ScheduleCommand(Command command){
			Commands.Enqueue(command);
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}
			
		public Boolean BulkInProgress{
			get {
				return bulkCommandsInProgress.IsEmpty == false;
			}
		}

		public Boolean AreTasksEnqueued{
			get {
				return Commands.IsEmpty == false;
			}
		}

		public Boolean AreRetryTasksEnqueued{
			get {
				return RetryCommands.IsEmpty == false;
			}
		}

		public List<int> errored = new List<int>();
		public async Task<EventManager> BulkUpload(){
			while (AreTasksEnqueued) {
				List<Command> tmpList = new List<Command> ();
				Command tmpCommand;
			
				while (tmpList.Count < 49 && Commands.TryDequeue (out tmpCommand)) {
					tmpList.Add (tmpCommand);
				}
				if (tmpList.Count > 0) {
					try{
						bulkCommandsInProgress.Enqueue(tmpList);
						var result = (await bulkUpload (Target,tmpList));


						foreach(var item in result){
							if(item.Result == BulkResult.Ok){
								SuccessfullCommands.Enqueue (item);
							}else if(item.Result == BulkResult.Retry){
								RetryCommands.Enqueue(item);
							}else {
								ErroredCommands.Enqueue(item);
							}
						}
					}catch(System.Net.WebException e){
						if (exceptionEvaluator (e)) {
							foreach (var item in tmpList) {
								RetryCommands.Enqueue (item);
							}
						} else {
							foreach (var item in tmpList) {
								ErroredCommands.Enqueue (item);
							}

						}
					}
				}
				List<Command> tmp;
				bulkCommandsInProgress.TryDequeue(out tmp);

			}
			return this;
		} 


	}
}