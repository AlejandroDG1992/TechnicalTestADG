using System;
using System.Collections.Generic;
using EntitiesXrm;
using Microsoft.Xrm.Sdk;
using FakeXrmEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TechnicalTestCRM_CodeActivityAlejandroDelgado;
using Xunit;
using Assert = Xunit.Assert;
using Microsoft.Xrm.Sdk.Query;

namespace WorkflowsTestProject
{
    public class CascadeSetStateTest
    {
        private static XrmFakedContext context { get; set; }
        private static XrmFakedWorkflowContext workflowContext;
        private static XrmFakedTracingService tracingService;
        private static IOrganizationService service;

        public CascadeSetStateTest()
        {
            context = new XrmFakedContext();
            tracingService = new XrmFakedTracingService();
            service = context.GetOrganizationService();
            workflowContext = context.GetDefaultWorkflowContext();
        }

        [Fact]
        [Trait("TestCategory", "UnitTest")]
        [Trait("Description", "When set correct inputs, the cascade execute the changes correctly")]
        public void When_Account_Was_Inactive_Cascade_Set_Inactive_Teachers()
        {
            //Create Data
            var profesorId = CreateTestData();

            var profesorBeforeCascadeOperation = (Profesor)service.Retrieve(Profesor.EntityLogicalName,
                profesorId, new ColumnSet("statecode"));

            var inputs = new Dictionary<string, object>() {
            { "ChildEntityName", "contact"},
            { "ChildLookupAttributeToParent", "parentcustomerid" },
            { "TargetChildStateCode", 1 }
            };

            context.ExecuteCodeActivity(workflowContext, inputs, new CascadeSetState());

            var profesorAfterCascadeOperation = (Profesor)service.Retrieve(Profesor.EntityLogicalName,
                profesorId, new ColumnSet("statecode"));

            Assert.NotEqual(profesorBeforeCascadeOperation.Estado, profesorAfterCascadeOperation.Estado);
        }

        [Fact]
        [Trait("TestCategory", "UnitTest"), Trait("TestCategory", "Sales")]
        [Trait("Description", "When the inpunts contains incorrect parameters return exception")]
        public void When_Send_Incorrect_Relatioship_Return_Exception()
        {
            //Create Data
            var profesorId = CreateTestData();

            var inputs = new Dictionary<string, object>() {
            { "ChildEntityName", "parentcustomerid"},
            { "ChildLookupAttributeToParent", "contact" },
            { "TargetChildStateCode", 1 }
            };

            Assert.Throws<Exception>(() =>
            context.ExecuteCodeActivity(workflowContext, inputs, new CascadeSetState()));
        }

        Guid CreateTestData()
        {
            var escuela = new Escuela();
            service.Create(escuela);

            var profesor = new Profesor()
            { 
                Nombredelaescuela = escuela.ToEntityReference() 
            };

            return service.Create(profesor);
        }
    }
}
