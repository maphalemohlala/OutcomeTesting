using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Counts the queries per table on the way to the fake.</summary>
    public sealed class CountingOrganizationService : IOrganizationService
    {
        private readonly IOrganizationService _inner;
        private readonly Dictionary<string, int> _queries = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public CountingOrganizationService(IOrganizationService inner)
        {
            _inner = inner;
        }

        public int QueriesOf(string entity)
        {
            int count;
            return _queries.TryGetValue(entity, out count) ? count : 0;
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = query as QueryExpression;
            if (expression != null)
            {
                int count;
                _queries.TryGetValue(expression.EntityName, out count);
                _queries[expression.EntityName] = count + 1;
            }

            return _inner.RetrieveMultiple(query);
        }

        public Guid Create(Entity entity) { return _inner.Create(entity); }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { return _inner.Retrieve(entityName, id, columnSet); }

        public void Update(Entity entity) { _inner.Update(entity); }

        public void Delete(string entityName, Guid id) { _inner.Delete(entityName, id); }

        public OrganizationResponse Execute(OrganizationRequest request) { return _inner.Execute(request); }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            _inner.Associate(entityName, entityId, relationship, relatedEntities);
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            _inner.Disassociate(entityName, entityId, relationship, relatedEntities);
        }
    }
}
