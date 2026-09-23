using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Keeps AQS reviewers in the AQS queue (AD-218, AR-03). The queue is an Account-scoped
    /// portal permission, so a reviewer reads it only while their contact's parent account is
    /// the AQS Team account. Granting the web role does not set that on its own, and a reviewer
    /// granted the role after rollout saw an empty queue in DEV.
    ///
    /// Registered synchronous post-operation on the Associate and Disassociate messages, which
    /// carry no primary entity, so every path that grants the role is covered: al_AssignUserRole,
    /// al_SetRoleAssignmentActive, al_AdoptRoleAssignment and the Power Pages admin screens,
    /// which associate from the role side. Anything but the contact-to-web-role link is ignored.
    /// </summary>
    public class AqsQueueMembershipPlugin : PluginBase
    {
        private const string ParentAttr = "parentcustomerid";

        public AqsQueueMembershipPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AqsQueueMembershipPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            object raw;
            var relationship = context.InputParameters.TryGetValue("Relationship", out raw) ? raw as Relationship : null;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as EntityReference : null;
            var related = context.InputParameters.TryGetValue("RelatedEntities", out raw) ? raw as EntityReferenceCollection : null;
            var associating = string.Equals(context.MessageName, "Associate", StringComparison.OrdinalIgnoreCase);

            // SYSTEM: the caller may be a portal administrator with no right to edit contacts;
            // the queue membership follows from the role, not from what the caller may write.
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            Apply(system, associating, relationship, target, related);
        }

        public static void Apply(
            IOrganizationService service,
            bool associating,
            Relationship relationship,
            EntityReference target,
            EntityReferenceCollection related)
        {
            if (relationship == null || target == null || related == null
                || !string.Equals(relationship.SchemaName, WebRoleRegistry.ContactRelationship, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var contacts = new List<Guid>();
            var roles = new List<Guid>();
            if (string.Equals(target.LogicalName, "contact", StringComparison.OrdinalIgnoreCase))
            {
                contacts.Add(target.Id);
                foreach (var r in related)
                {
                    roles.Add(r.Id);
                }
            }
            else
            {
                roles.Add(target.Id);
                foreach (var r in related)
                {
                    contacts.Add(r.Id);
                }
            }

            if (!AnyIsAqsReviewer(service, roles))
            {
                return;
            }

            var queue = associating ? CaseAccessReconciler.AqsQueueAccount(service) : null;
            foreach (var contactId in contacts)
            {
                var parent = service.Retrieve("contact", contactId, new ColumnSet(ParentAttr))
                    .GetAttributeValue<EntityReference>(ParentAttr);

                if (associating)
                {
                    Join(service, contactId, parent, queue);
                }
                else
                {
                    Leave(service, contactId, parent);
                }
            }
        }

        private static void Join(IOrganizationService service, Guid contactId, EntityReference parent, EntityReference queue)
        {
            if (parent != null && parent.Id == queue.Id)
            {
                return;
            }

            if (parent != null)
            {
                // Moving the contact would silently take away whatever its account grants;
                // leaving it would grant a role whose queue stays empty. Refusing says which.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix
                    + "An AQS reviewer reads the AQS queue through the \"" + CaseAccessReconciler.AqsQueueAccountName
                    + "\" account (AD-218), but this person's contact already belongs to another account. "
                    + "Clear their company on the contact, then grant the role again.");
            }

            service.Update(new Entity("contact", contactId) { [ParentAttr] = queue });
        }

        private static void Leave(IOrganizationService service, Guid contactId, EntityReference parent)
        {
            if (parent == null || !string.Equals(parent.LogicalName, "account", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Looked up only when there is a parent to compare, so a withdrawal never fails
            // on a missing queue account.
            var queue = FindQueueAccountOrNull(service);
            if (queue == null || parent.Id != queue.Value)
            {
                return;
            }

            service.Update(new Entity("contact", contactId) { [ParentAttr] = null });
        }

        private static Guid? FindQueueAccountOrNull(IOrganizationService service)
        {
            var query = new QueryExpression("account") { ColumnSet = new ColumnSet(false), TopCount = 2 };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, CaseAccessReconciler.AqsQueueAccountName);
            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 1 ? rows[0].Id : (Guid?)null;
        }

        /// <summary>
        /// Every web role row carrying the AQS Reviewer name, not an arbitrary one, so a second
        /// row sharing the name cannot make a genuine grant look like another role.
        /// </summary>
        private static bool AnyIsAqsReviewer(IOrganizationService service, List<Guid> roleIds)
        {
            if (roleIds.Count == 0)
            {
                return false;
            }

            var fetch =
                "<fetch>" +
                  "<entity name='" + WebRoleRegistry.RoleEntity + "'>" +
                    "<attribute name='" + WebRoleRegistry.NameAttr + "'/>" +
                    "<filter><condition attribute='" + WebRoleRegistry.NameAttr + "' operator='eq' value='" +
                      System.Security.SecurityElement.Escape(WebRoleRegistry.AqsReviewerRole) + "'/></filter>" +
                  "</entity>" +
                "</fetch>";

            foreach (var role in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                if (roleIds.Contains(role.Id))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
