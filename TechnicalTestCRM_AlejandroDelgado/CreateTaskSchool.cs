using EntitiesXrm;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace TechnicalTestCRM_AlejandroDelgado
{
    public class CreateTaskSchool : IPlugin
    {
        public CreateTaskSchool(string unsecureString, string secureString) {}

        public void Execute(IServiceProvider serviceProvider)
        {
            // Obtain the tracing service
            ITracingService tracingService =
            (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            IPluginExecutionContext context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));

            IOrganizationServiceFactory serviceFactory = 
                (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            if (context.InputParameters.Contains("Target") &&
                context.InputParameters["Target"] is Entity)
            {
                // Obtain the target entity from the input parameters.  
                Entity entity = (Entity)context.InputParameters["Target"];

                try
                {
                    // Create a task activity starts in 10 days.
                    var taskToCreate = new Entity("task");
                    taskToCreate["subject"] = "Acto de inauguración.";
                    taskToCreate["description"] =
                        "En 10 días ha de cortar el cordón de inauguración y aún no ha comprado las tijeras.";
                    taskToCreate["scheduledstart"] = DateTime.Now.AddDays(10);
                    taskToCreate["scheduledend"] = DateTime.Now.AddDays(10);
                    taskToCreate["category"] = context.PrimaryEntityName;
                    taskToCreate["regardingobjectid"] =
                        new EntityReference(entity.LogicalName, entity.Id);

                    tracingService.Trace("CreateTaskSchool: Creando la tarea.");
                    service.Create(taskToCreate);
                }
                catch (Exception ex)
                {
                    tracingService.Trace("CreateTaskSchool: {0}", ex.ToString());
                    throw new InvalidPluginExecutionException("Ha ocurrido un error.", ex);
                }
            }
        }
    }
}
