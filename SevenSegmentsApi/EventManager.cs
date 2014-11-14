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
using System.Runtime.Serialization;

namespace SevenSegmentsApi
{


	public enum BulkResult {Ok, Error, Retry};

	public abstract class Command {

		protected static String serializeManualy(Dictionary<String,String> dict){
			return "{" + String.Join (",\n", from item in dict
				select ("\""+item.Key + "\":\"" + item.Value+"\"")) + "}";
		}

		public String Company;
		public String Customer;
		public Dictionary<String,String> Properties;

		public abstract String Endpoint {
			get ;
		}

		public abstract String JsonPayload {
			get ;
		}

	}

	public class Customer : Command {
		public Customer (String Company,String Customer,Dictionary<String,String> Properties)
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

		public override String JsonPayload {
			get { 
				if (Properties != null) {
					return "{\"ids\" : {\"registered\" : \"" + Customer + "\"},\n\"company_id\": \"" + Company + "\",\n" + "\"properties\" : " + serializeManualy (Properties) + "}";
				} else {
					return "{\"ids\" : {\"registered\" : \"" + Customer + "\"},\n\"company_id\": \"" + Company + "\"}";
				}
			}
		}


	}

	public class Event : Command {
		public String Type;
		public long time;
		public long age;

		public Event (String Company,String Customer,String Type,Dictionary<String,String> Properties, long time)
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
		public override String JsonPayload {
			get { 
				if (Properties != null) {
					return "{\"customer_ids\" : {\"registered\" : \"" + Customer + "\"},\n\"company_id\": \"" + Company + "\",\n" +"\"type\":\"" + Type + "\", \"properties\" : " + serializeManualy(Properties) +", \"age\":" + this.age +"}";
				} else {
					return "{\"customer_ids\" : {\"registered\" : \"" + Customer + "\"},\n\"company_id\": \"" + Company +"\",\n" +"\"type\":\"" + Type +"\", \"age\":" + this.age + "}";
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


		private static async Task<List<Tuple<BulkResult,String>>> bulkUpload(Uri target, List<Command> bulk ){
			long currentTime = Epoch ();

			foreach (var item in bulk){
				if (item is Event) {
					var c = (Event)item;
					c.age = currentTime - c.time;
				}
			}
			String payload =  "{\"commands\": [" + String.Join (",\n", from item in bulk
				select ("{\"name\":\""+item.Endpoint+"\", \"data\":"+item.JsonPayload+"}")) + "]}";
			String result = await post (target,payload);

			return deserializeBulkManualy (result);
		}

		private static List<Tuple<BulkResult,String>> deserializeBulkManualy(String input){
			string[] s = input.Split (new Char[]{'['}, 2);
			if (s.Length < 2) {
				return null;
			}

			Regex regex_ok = new Regex(@"status.*ok");
			Regex regex_retry = new Regex(@"status.*retry");


			input = s [1];
			int l = 0;
			List<Tuple<BulkResult,String>> commandStatus = new List<Tuple<BulkResult,String>>();
			String buffer = "";
			foreach (Char c in input.ToCharArray()) {
				if (c == '{')
					l++;
				if (c == '}')
					l--;
				buffer += c;
				if ((l == 0) && (c == '}')) {
					var tmpBulkResult = BulkResult.Error;
					Match mok = regex_ok.Match (buffer);
					if (mok.Success) {
						tmpBulkResult = BulkResult.Ok;
					} else {
						Match mretr = regex_retry.Match (buffer);
						if (mretr.Success) {
							tmpBulkResult = BulkResult.Retry;
						}
					}
					commandStatus.Add (new Tuple<BulkResult,String>(tmpBulkResult, buffer.Trim()));
					l = 0;
					buffer = "";
				}
			}
			return commandStatus;

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
			exceptionEvaluator = async (x) => {return false;};
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
			exceptionEvaluator = async (x) => {return false;};
			CompanyToken = companyToken;
			Target = target;
			Customer = customer;
			AutomaticUpload = automaticUpload;
		}

		public Task<EventManager> Update(Dictionary<String,String> properties){
			this.ScheduleCustomer (CompanyToken, Customer, properties);
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

		public Task<EventManager> Identify(String customer,Dictionary<String,String> properties){
			Customer = customer;
			ScheduleCustomer (CompanyToken, customer, properties);
			if (AutomaticUpload) {
				return BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
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

		private Func<WebException,Task<Boolean>> exceptionEvaluator;
		public EventManager SetRetryOnException(Func<WebException,Task<Boolean>> evaluator){
			exceptionEvaluator = evaluator;
			return this;
		}

		private Task<EventManager> ScheduleCustomer(String Company,String Customer,Dictionary<String,String> Properties){
			Commands.Enqueue(new Customer( Company, Customer, Properties));
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}
	
		public Task<EventManager> Track(String Type,Dictionary<String,String> Properties, long Time){
			Commands.Enqueue(new Event( CompanyToken, Customer, Type, Properties, Time));
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

		public Task<EventManager> Track(String Type, long Time){
			Commands.Enqueue(new Event( CompanyToken, Customer, Type, null, Time));
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

	
		public Task<EventManager> Track(String Type){
			Commands.Enqueue(new Event( CompanyToken, Customer, Type, null, Epoch()));
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

		public Task<EventManager> Track(String Type,Dictionary<String,String> Properties){
			Commands.Enqueue(new Event( CompanyToken, Customer, Type, Properties, Epoch()));
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

		private Task<EventManager> ScheduleCommand(Command command){
			Commands.Enqueue(command);
			if (AutomaticUpload) {
				return this.BulkUpload ();
			} else {
				return Task.FromResult (this);
			}
		}

		private Task<EventManager> ScheduleCommands(List<Command> commandList){
			foreach(var command in commandList){
				Commands.Enqueue(command);
			}
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

		public async Task<EventManager> RetryBulkUpload(){
			if (AreRetryTasksEnqueued) {
				List<Command> tmpList = new List<Command> ();
				Command tmpCommand;
				while (tmpList.Count < 49 && RetryCommands.TryDequeue (out tmpCommand)) {
					tmpList.Add (tmpCommand);
				}

				if (tmpList.Count > 0) {
					try{

						var result = (await bulkUpload (Target,tmpList)).Select ((item, n) => new {Value = item, Index = n}).ToList();

						var retry = result.Where (item => item.Value.Item1 == BulkResult.Retry).Select (item => item.Index);
						foreach (var item in tmpList.Where ((item, n) => retry.Contains (n))) {
							RetryCommands.Enqueue (item);
						}
						var done = result.Where (item => item.Value.Item1 == BulkResult.Ok).Select (item => item.Index);
						foreach (var item in tmpList.Where ((item, n) => done.Contains (n))) {
							SuccessfullCommands.Enqueue (item);
						}
						var errored = result.Where (item => item.Value.Item1 == BulkResult.Error).Select (item => item.Index);
						foreach(var item in tmpList.Where ((item, n) => errored.Contains (n))){
							ErroredCommands.Enqueue (item);
						}
					}catch(System.Net.WebException e){
						if (await exceptionEvaluator (e)) {
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
			}
			return this;
		} 
		public List<Tuple<BulkResult,String>> rresult;
		public List<int> errored = new List<int>();
		public async Task<EventManager> BulkUpload(){
			if (AreTasksEnqueued) {
				List<Command> tmpList = new List<Command> ();
				Command tmpCommand;
			
				while (tmpList.Count < 49 && Commands.TryDequeue (out tmpCommand)) {
					tmpList.Add (tmpCommand);
				}
				if (tmpList.Count > 0) {
					try{
						bulkCommandsInProgress.Enqueue(tmpList);
						var result = (await bulkUpload (Target,tmpList)).Select ((item, n) => new {Value = item, Index = n}).ToList();

						var retry = result.Where (item => item.Value.Item1 == BulkResult.Retry).Select (item => item.Index);
						foreach (var item in tmpList.Where ((item, n) => retry.Contains (n))) {
							RetryCommands.Enqueue (item);
						}
						var done = result.Where (item => item.Value.Item1 == BulkResult.Ok).Select (item => item.Index);
						foreach (var item in tmpList.Where ((item, n) => done.Contains (n))) {
							SuccessfullCommands.Enqueue (item);
						}
						errored = result.Where (item => item.Value.Item1 == BulkResult.Error).Select (item => item.Index).ToList();
						foreach(var item in tmpList.Where ((item, n) => errored.Contains (n))){
								ErroredCommands.Enqueue (item);
						}
					}catch(System.Net.WebException e){
						if (await exceptionEvaluator (e)) {
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