using System;
using System.Collections.Generic;
using EntitiesXrm;
using Microsoft.Xrm.Sdk;
using FakeXrmEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TechnicalTestCRM_CodeActivityAlejandroDelgado;
using Xunit;
using Assert = Xunit.Assert;

namespace WorkflowsTestProject
{
    [TestClass]
    public class CreateMailDirectorTest
    {
        private static XrmFakedContext context { get; set; }
        private static XrmFakedWorkflowContext workflowContext;
        private static XrmFakedTracingService tracingService;
        private static IOrganizationService service;

        public CreateMailDirectorTest()
        {
            context = new XrmFakedContext();
            tracingService = new XrmFakedTracingService();
            service = context.GetOrganizationService();
            workflowContext = context.GetDefaultWorkflowContext();
        }

        [Fact]
        [Trait("TestCategory", "UnitTest"), Trait("TestCategory", "Sales")]
        [Trait("Description", "The SendEmailFromTemplateResponse isn't supported")]
        public void WhenExecuteTestSetPullRequestException()
        {
            var director = new Profesor()
            {
                Id = new Guid(),
                Correoelectronico = "aaa@test.com"
            };

            var directorId = service.Create(director);            

            var inputs = new Dictionary<string, object>() {
            { "TemplateMailId", new Guid().ToString()},
            { "ContactToSendEmail", new EntityReference(Profesor.EntityLogicalName, directorId) }
            };

            Assert.Throws<PullRequestException>(() => 
            context.ExecuteCodeActivity(workflowContext, inputs, new CreateMailDirector()));
        }
    }
}
