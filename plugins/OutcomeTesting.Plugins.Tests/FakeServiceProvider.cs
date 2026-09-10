using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.PluginTelemetry;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The bare service provider <see cref="PluginBase"/> needs to run, so the pipeline's own
    /// behaviour - what it does with an exception on its way out - can be tested rather than
    /// only the rules the plug-ins call.
    ///
    /// Everything here is the minimum <see cref="PluginBase.LocalPluginContext"/> asks for.
    /// The optional services it looks up (<see cref="ILogger"/>, the endpoint notification
    /// service) are answered with null, which is what a real provider does for a plug-in that
    /// is not registered to use them.
    /// </summary>
    public sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly FakeOrganizationService _service;
        private readonly FakePluginExecutionContext _context;

        public FakeServiceProvider(FakeOrganizationService service = null)
        {
            _service = service ?? new FakeOrganizationService();
            _context = new FakePluginExecutionContext();
        }

        /// <summary>Everything the plug-in traced, in order.</summary>
        public System.Collections.Generic.List<string> Traces { get; } =
            new System.Collections.Generic.List<string>();

        public FakePluginExecutionContext Context
        {
            get { return _context; }
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext))
            {
                return _context;
            }

            if (serviceType == typeof(ITracingService))
            {
                return new FakeTracingService(Traces);
            }

            if (serviceType == typeof(IOrganizationServiceFactory))
            {
                return new FakeOrganizationServiceFactory(_service);
            }

            // ILogger and IServiceEndpointNotificationService are optional; a provider
            // returns null for a plug-in that is not registered to use them.
            return null;
        }

        private sealed class FakeTracingService : ITracingService
        {
            private readonly System.Collections.Generic.List<string> _traces;

            public FakeTracingService(System.Collections.Generic.List<string> traces)
            {
                _traces = traces;
            }

            public void Trace(string format, params object[] args)
            {
                _traces.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
        }

        private sealed class FakeOrganizationServiceFactory : IOrganizationServiceFactory
        {
            private readonly FakeOrganizationService _service;

            public FakeOrganizationServiceFactory(FakeOrganizationService service)
            {
                _service = service;
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                return _service;
            }
        }
    }
}
