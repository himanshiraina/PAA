using System.Threading.Tasks;
using Xrm.Tools.WebAPI;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Xrm.Tools.WebAPI.Results;
using System.Dynamic;
using Xrm.Tools.WebAPI.Requests;
using System.Collections.Generic;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;

namespace InspectionRoutingApp
{
    public class InspectionRouter
    {

        private readonly ILogger _logger;

        public InspectionRouter(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<InspectionRouter>();
        }

        [Function("InspectionRouter")]
        public void Run([TimerTrigger("0 0 0 * * *")] MyInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            CRMWebAPI api = GetCRMWebAPI(_logger).Result;
            dynamic whoami = api.ExecuteFunction("WhoAmI").Result;
            _logger.LogInformation($"UserID: {whoami.UserId}");
            _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            var inspections = GetInspections(api).Result;
            var users = GetUsers(api).Result;
            Random rnd = new Random();
            int sequenceNumber = rnd.Next(10, 99);
            int currentUserIndex = 0;
            foreach (dynamic inspection in inspections.List)
            {
                _logger.LogInformation($"Routing inspection {inspection.contoso_name}");
                var inspectionResult = new CRMUpdateResult();
                // Your record assignment would look like this. We will not assign records to different users in this lab
                // if (users.List.Count > (currentUserIndex))
                //{
                // dynamic currentUser = users.List[currentUserIndex];
                // inspectionResult = RouteInspection(api, inspection, currentUser.systemuserid.ToString(), sequenceNumber).Result;
                //currentUserIndex++;
                //}
                // We will instead assign inspections to the user you are currently logged in as
                inspectionResult = RouteInspection(api, inspection, whoami.UserId.ToString(), sequenceNumber).Result;
            }
        }
        private static async Task<CRMWebAPI> GetCRMWebAPI(ILogger log)
        {
            var clientID = Environment.GetEnvironmentVariable("cdsclientid", EnvironmentVariableTarget.Process);
            var clientSecret = Environment.GetEnvironmentVariable("cdsclientsecret", EnvironmentVariableTarget.Process);
            var crmBaseUrl = Environment.GetEnvironmentVariable("cdsurl", EnvironmentVariableTarget.Process);
            var crmurl = crmBaseUrl + "/api/data/v9.2/";
            AuthenticationParameters ap = await AuthenticationParameters.CreateFromUrlAsync(new Uri(crmurl));
            var clientcred = new ClientCredential(clientID, clientSecret);
            // CreateFromUrlAsync returns endpoint while AuthenticationContext expects authority
            // workaround is to downgrade adal to v3.19 or to strip the tail
            var auth = ap.Authority.Replace("/oauth2/authorize", "");
            var authContext = new AuthenticationContext(auth);
            var authenticationResult = await authContext.AcquireTokenAsync(crmBaseUrl, clientcred);
            return new CRMWebAPI(crmurl, authenticationResult.AccessToken);
        }
        private static Task<CRMGetListResult<ExpandoObject>> GetInspections(CRMWebAPI api)
        {
            var fetchxml = @"<fetch version=""1.0"" mapping=""logical"" >
    <entity name=""contoso_inspection"" >
       <attribute name=""contoso_inspectionid"" />
       <attribute name=""contoso_name"" />
       <attribute name=""ownerid"" />
       <attribute name=""contoso_inspectiontype"" />
       <attribute name=""contoso_sequence"" />
       <attribute name=""contoso_scheduleddate"" />
       <filter type=""and"" >
          <condition value=""0"" operator=""eq"" attribute=""statecode"" />
          <condition attribute=""contoso_scheduleddate"" operator=""today"" />
          <condition attribute=""statuscode"" operator=""in"" >
             <value>1</value>
             <value>330650001</value>
          </condition>
       </filter>
    </entity>
 </fetch>";
            var inspections = api.GetList<ExpandoObject>("contoso_inspections", QueryOptions: new CRMGetListOptions()
            {
                FetchXml = fetchxml
            });
            return inspections;
        }
        private static Task<CRMGetListResult<ExpandoObject>> GetUsers(CRMWebAPI api)
        {
            var users = api.GetList<ExpandoObject>("systemusers");
            return users;
        }
        private static async Task<CRMUpdateResult> RouteInspection(CRMWebAPI api, dynamic inspection, string userId, int sequenceNumber)
        {
            dynamic updateObject = new ExpandoObject();
            ((IDictionary<string, object>)updateObject).Add("ownerid@odata.bind", "/systemusers(" + userId + ")");
            updateObject.contoso_sequence = sequenceNumber.ToString();
            return await api.Update("contoso_inspections", new Guid(inspection.contoso_inspectionid), updateObject);
        }


    }

    public class MyInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}