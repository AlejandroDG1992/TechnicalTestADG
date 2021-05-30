using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using System;
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;

namespace TechnicalTestCRM_CodeActivityAlejandroDelgado
{
    public class CreateMailDirector : CodeActivity
    {
        [RequiredArgument]
        [Input("TemplateMailId")]
        public InArgument<string> TemplateMailId { get; set; }

        [RequiredArgument]
        [ReferenceTarget("contact")]
        [Input("ContactToSendEmail")]
        public InArgument<EntityReference> ContactToSendEmail { get; set; }

        protected override void Execute(CodeActivityContext executionContext)
        {
            IWorkflowContext context = executionContext.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = executionContext.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.InitiatingUserId);

            var director = ContactToSendEmail.Get(executionContext);

            //Create the ‘To:’ activity party for the email
            var toParty = new Entity("activityparty");
            toParty["partyid"] = new EntityReference(director.LogicalName , director.Id);

            //Create an e-mail message content.
            var email = new Entity("email");
            email["to"] = new Entity[1] { toParty };
            email["subject"] = "SDK Sample e - mail";
            email["description"] = "SDK Sample for SendEmailFromTemplate Message.";
            email["directioncode"] = true;

            // Create the request of template
            SendEmailFromTemplateRequest emailUsingTemplateReq = new SendEmailFromTemplateRequest
            {
                Target = email,
                TemplateId = new Guid(TemplateMailId.Get(executionContext)),
                RegardingId = director.Id,
                RegardingType = director.LogicalName
            };

            SendEmailFromTemplateResponse emailUsingTemplateResp = 
                (SendEmailFromTemplateResponse)service.Execute(emailUsingTemplateReq);
        }
    }
}
