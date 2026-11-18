using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A plug-in host whose organisation services run the table guards on every write, as
    /// the platform does, under the identity each service writes as: the caller for
    /// <c>CreateOrganizationService(userId)</c>, SYSTEM for <c>CreateOrganizationService(null)</c>.
    /// FakeServiceProvider hands every caller the same unguarded service, so it cannot show
    /// that a command writes a guarded column through the right one.
    /// </summary>
    public sealed class GuardedPipeline : IServiceProvider
    {
        private readonly FakeOrganizationService _store;

        public GuardedPipeline(FakeOrganizationService store)
        {
            _store = store;
        }

        public FakePluginExecutionContext Context { get; } = new FakePluginExecutionContext();

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext))
            {
                return Context;
            }

            if (serviceType == typeof(ITracingService))
            {
                return new NoTrace();
            }

            if (serviceType == typeof(IOrganizationServiceFactory))
            {
                return new Factory(this);
            }

            return null;
        }

        private sealed class NoTrace : ITracingService
        {
            public void Trace(string format, params object[] args)
            {
            }
        }

        private sealed class Factory : IOrganizationServiceFactory
        {
            private readonly GuardedPipeline _host;

            public Factory(GuardedPipeline host)
            {
                _host = host;
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                return new GuardedService(
                    _host._store, userId ?? FakeOrganizationService.SystemUserId, _host.Context.InitiatingUserId);
            }
        }

        /// <summary>Runs the guard registered on the table before each create and update.</summary>
        public sealed class GuardedService : IOrganizationService
        {
            private readonly FakeOrganizationService _store;
            private readonly Guid _user;
            private readonly Guid _initiating;

            public GuardedService(FakeOrganizationService store, Guid user, Guid initiating)
            {
                _store = store;
                _user = user;
                _initiating = initiating;
            }

            private void Guard(string message, Entity target)
            {
                IPlugin guard;
                switch (target.LogicalName)
                {
                    case "al_outcome":
                        guard = new OutcomeGuardPlugin(null, null);
                        break;
                    case "al_reviewinstance":
                        guard = new ReviewSubmissionGuardPlugin(null, null);
                        break;
                    case "al_caseassignment":
                        guard = new ClaimCasePlugin(null, null);
                        break;
                    default:
                        return;
                }

                var provider = new FakeServiceProvider(_store);
                provider.Context.MessageName = message;
                provider.Context.PrimaryEntityName = target.LogicalName;
                provider.Context.PrimaryEntityId = target.Id;
                provider.Context.InputParameters["Target"] = target;
                provider.Context.UserId = _user;
                provider.Context.InitiatingUserId = _initiating;
                guard.Execute(provider);
            }

            public Guid Create(Entity entity) { Guard("Create", entity); return _store.Create(entity); }
            public void Update(Entity entity) { Guard("Update", entity); _store.Update(entity); }
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { return _store.Retrieve(entityName, id, columnSet); }
            public EntityCollection RetrieveMultiple(QueryBase query) { return _store.RetrieveMultiple(query); }
            public void Delete(string entityName, Guid id) { _store.Delete(entityName, id); }
            public OrganizationResponse Execute(OrganizationRequest request) { return _store.Execute(request); }
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _store.Associate(entityName, entityId, relationship, relatedEntities); }
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { _store.Disassociate(entityName, entityId, relationship, relatedEntities); }
        }
    }
}
