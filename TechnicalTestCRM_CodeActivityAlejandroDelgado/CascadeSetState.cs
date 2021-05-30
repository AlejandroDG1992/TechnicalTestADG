using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Activities;
using System.Collections.Specialized;
using System.Web;
using Microsoft.Xrm.Sdk.Workflow;
using Microsoft.Crm.Sdk.Messages;

namespace TechnicalTestCRM_CodeActivityAlejandroDelgado
{
    public sealed class CascadeSetState : CodeActivity
    {
        [Input("Child Entity Name")]
        [RequiredArgument]
        public InArgument<string> ChildEntityName { get; set; }

        [Input("Child Lookup Attribute To Parent")]
        [RequiredArgument]
        public InArgument<string> ChildLookupAttributeToParent { get; set; }

        [Input("Target Child State Code")]
        [RequiredArgument]
        public InArgument<int> TargetChildStateCode { get; set; }


        protected override void Execute(CodeActivityContext activityContext)
        {
            ITracingService tracingService = activityContext.GetExtension<ITracingService>();
            IExecutionContext context = activityContext.GetExtension<IExecutionContext>();
            IOrganizationServiceFactory serviceFactory = activityContext.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            Guid primaryEntityId = context.PrimaryEntityId;
            string childEntityName = ChildEntityName.Get(activityContext);
            string childLookupAttributeToParent = ChildLookupAttributeToParent.Get(activityContext);
            int targetChildStateCode = TargetChildStateCode.Get(activityContext);

            QueryExpression query = new QueryExpression(childEntityName);
            query.Criteria.AddCondition(new ConditionExpression(childLookupAttributeToParent, ConditionOperator.Equal, primaryEntityId));
            query.Criteria.AddCondition(new ConditionExpression("statecode", ConditionOperator.NotEqual, targetChildStateCode));
            query.PageInfo = new PagingInfo();
            query.PageInfo.PageNumber = 1;
            query.PageInfo.Count = 100;

            ExecuteMultipleRequest emReq;

            RetrieveMultipleRequest req = new RetrieveMultipleRequest()
            {
                Query = query,
            };
            bool moreRecords = true;
            while (moreRecords)
            {
                emReq = new ExecuteMultipleRequest()
                {
                    // Assign settings that define execution behavior: continue on error, return responses. 
                    Settings = new ExecuteMultipleSettings()
                    {
                        ContinueOnError = true,
                        ReturnResponses = true
                    },
                    // Create an empty organization request collection.
                    Requests = new OrganizationRequestCollection()
                };
                RetrieveMultipleResponse resp = (RetrieveMultipleResponse)service.Execute(req);
                foreach (Entity e in resp.EntityCollection.Entities)
                {
                    //Set record state
                    SetStateRequest setStateReq = new SetStateRequest
                    {
                        EntityMoniker = e.ToEntityReference(),
                        State = new OptionSetValue(targetChildStateCode),
                        Status = new OptionSetValue(-1),
                    };
                    emReq.Requests.Add(setStateReq);
                }

                ExecuteMultipleResponse emResp = (ExecuteMultipleResponse)service.Execute(emReq);

                // Verify results of the requests
                foreach (var responseItem in emResp.Responses)
                {
                    // An error has occurred.
                    if (responseItem.Fault != null)
                    {
                        string errorMessage = string.Format("Error in cascade set state for record type {0} and id {1}: {2}",
                            childEntityName,
                            ((SetStateRequest)emReq.Requests[responseItem.RequestIndex]).EntityMoniker.Id,
                            responseItem.Fault.ToString());
                        throw new InvalidPluginExecutionException(errorMessage);
                    }
                }

                // Set the paging information for reading the next page
                moreRecords = resp.EntityCollection.MoreRecords;
                query.PageInfo.PagingCookie = resp.EntityCollection.PagingCookie;
                query.PageInfo.PageNumber++;
            }
        }
    }
}