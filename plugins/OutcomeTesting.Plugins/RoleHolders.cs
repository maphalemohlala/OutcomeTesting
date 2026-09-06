using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>One person's relationship to one role, from both sources at once (AD-089).</summary>
    public sealed class RoleHolder
    {
        public string Email { get; set; }
        public string Name { get; set; }

        /// <summary>Null when no al_userrolemapping row exists — a portal-only grant.</summary>
        public Guid? MappingId { get; set; }

        /// <summary>Null when there is no mapping; otherwise whether that mapping is active.</summary>
        public bool? MappingActive { get; set; }

        /// <summary>Whether the contact is actually associated with the web role.</summary>
        public bool Associated { get; set; }
    }

    /// <summary>
    /// Joins the mapping table to the web role associations for one role.
    ///
    /// Pure on purpose: every case worth testing is a disagreement between the two
    /// sources, and none of them needs a service to express. The plug-in does the two
    /// reads and hands the rows here.
    ///
    /// Work email is the join key (AD-010), compared case-insensitively because the two
    /// sources are written by different paths and a difference of casing is not a
    /// different person.
    /// </summary>
    public static class RoleHolders
    {
        public static List<RoleHolder> Merge(
            IEnumerable<Entity> mappings,
            IEnumerable<Entity> associatedContacts)
        {
            var byEmail = new Dictionary<string, RoleHolder>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in mappings ?? Enumerable.Empty<Entity>())
            {
                var email = (mapping.GetAttributeValue<string>("al_useremail") ?? string.Empty).Trim();
                var holder = Find(byEmail, email);
                holder.MappingId = mapping.Id;
                holder.MappingActive = CommandHelpers.IsActive(mapping);
            }

            foreach (var contact in associatedContacts ?? Enumerable.Empty<Entity>())
            {
                var email = (contact.GetAttributeValue<string>("emailaddress1") ?? string.Empty).Trim();
                var holder = Find(byEmail, email);
                holder.Associated = true;

                var name = contact.GetAttributeValue<string>("fullname");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    holder.Name = name.Trim();
                }
            }

            return byEmail.Values.OrderBy(h => h.Email, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static RoleHolder Find(IDictionary<string, RoleHolder> byEmail, string email)
        {
            RoleHolder holder;
            if (!byEmail.TryGetValue(email, out holder))
            {
                holder = new RoleHolder { Email = email, Name = null, Associated = false };
                byEmail[email] = holder;
            }

            return holder;
        }

        /// <summary>
        /// The holders as a JSON array. Custom API response properties are String, Boolean
        /// or Integer only, so a list travels as JSON in a String — the precedent is
        /// al_ImportCases. Hand-built because the plug-in targets net462 with no serializer.
        /// </summary>
        public static string ToJson(IEnumerable<RoleHolder> holders)
        {
            var builder = new StringBuilder("[");
            var first = true;

            foreach (var holder in holders ?? Enumerable.Empty<RoleHolder>())
            {
                if (!first)
                {
                    builder.Append(",");
                }

                first = false;
                builder.Append("{\"email\":\"").Append(ImportRules.JsonEscape(holder.Email ?? string.Empty)).Append("\"");
                builder.Append(",\"name\":");
                builder.Append(holder.Name == null ? "null" : "\"" + ImportRules.JsonEscape(holder.Name) + "\"");
                builder.Append(",\"mappingId\":");
                builder.Append(holder.MappingId.HasValue ? "\"" + holder.MappingId.Value.ToString("D") + "\"" : "null");
                builder.Append(",\"mappingActive\":");
                builder.Append(holder.MappingActive.HasValue ? (holder.MappingActive.Value ? "true" : "false") : "null");
                builder.Append(",\"associated\":").Append(holder.Associated ? "true" : "false");
                builder.Append("}");
            }

            return builder.Append("]").ToString();
        }
    }
}
