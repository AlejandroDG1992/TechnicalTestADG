using System;
using System.Collections.Generic;
using FakeXrmEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xrm.Sdk;
using EntitiesXrm;
using TechnicalTestCRM_AlejandroDelgado;
using System.Linq;
using System.ServiceModel;
using Xunit;
using Assert = Xunit.Assert;

namespace PluginsTestProject
{
    [TestClass]
    public class CreateTaskSchoolTest
    {
        private XrmFakedContext context { get; set; }
        private XrmFakedPluginExecutionContext pluginExecutionContext { get; set; }
        private XrmFakedTracingService tracingService { get; set; }
        private IOrganizationService organizationService { get; set; }

        public CreateTaskSchoolTest()
        {
            context = new XrmFakedContext();
            tracingService = new XrmFakedTracingService();
            organizationService = context.GetOrganizationService();
            pluginExecutionContext = context.GetDefaultPluginContext();
        }

        [Fact]
        [Trait("TestCategory", "UnitTest")]
        [Trait("Description", "Create task when account was created")]
        public void Create_Task_When_Create_Account()
        {
            var school = new Escuela();
            var schoolId = organizationService.Create(school);
            var inputParams = new ParameterCollection { new KeyValuePair<string, object>("Target", school) };
            SetPluginContextParams(inputParams, 25.ToString(), Escuela.EntityLogicalName, 40, schoolId);

            var taskActual = (from t in context.CreateQuery<Tarea>() 
                              select t).ToList();

            context.ExecutePluginWithConfigurations<CreateTaskSchool>(pluginExecutionContext, string.Empty, string.Empty);

            var taskExpected = (from t in context.CreateQuery<Tarea>() 
                                select t).ToList();

            Assert.Equal(taskActual.Count + 1, taskExpected.Count);
        }

        void SetPluginContextParams(ParameterCollection inputParams, string messageName, string primaryEntityName, int stage, Guid primaryEntityId)
        {
            pluginExecutionContext.InputParameters = inputParams;
            pluginExecutionContext.MessageName = messageName;
            pluginExecutionContext.PrimaryEntityName = primaryEntityName;
            pluginExecutionContext.Stage = stage;
            pluginExecutionContext.PrimaryEntityId = primaryEntityId;
        }
    }
}
