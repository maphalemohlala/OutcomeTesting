using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Retired: the manual export batch command is gone. The type stays in the assembly only
    /// because TEST (and any environment that imported 1.0.41.0 or earlier) registers it as a managed plug-in type, and Dataverse refuses any
    /// assembly update that drops a registered type unless the major/minor version changes.
    /// Nothing is bound to it; if something ever is, it refuses rather than doing work.
    /// </summary>
    public class CreateExportBatchPlugin : PluginBase
    {
        public CreateExportBatchPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CreateExportBatchPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            throw new InvalidPluginExecutionException("Manual export batches are no longer available.");
        }
    }
}
