using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Option-set labels, for the prose a person actually reads (FR-033).
    ///
    /// <c>al_details</c> on an Audit Event is rendered verbatim on the case history screen.
    /// A line reading "Status 120910583 -> 120910584" names two facts nobody outside the
    /// schema can translate, and there is no second place for the reader to go and look
    /// them up. The label has to be resolved when the line is WRITTEN, because an Audit
    /// Event is immutable (NFR-AUD-01): rows already written keep the numbers they carry,
    /// and no later fix can reach them.
    ///
    /// The number is kept as the fallback rather than replaced by a placeholder. An option
    /// minted after this row was written, or one since removed, still says more as a number
    /// than as "(unknown)".
    ///
    /// Metadata is read once per (entity, attribute) and cached for the life of the
    /// instance, which is one plug-in execution — so a command that changes six choice
    /// columns spends six metadata reads at most, and a replayed one spends none.
    ///
    /// Deliberately no try/catch around the metadata read. A plug-in that swallows a fault
    /// from an OrganizationService call and carries on has its whole transaction aborted by
    /// the platform ("ISV code reduced the open transaction count"), so "fall back to the
    /// number on failure" is not something a catch here can deliver. Pass the plug-in user's
    /// service instead: metadata is readable to it, so there is no failure to absorb.
    /// </summary>
    public sealed class OptionLabels
    {
        private readonly IOrganizationService _service;

        private readonly Dictionary<string, Dictionary<int, string>> _cache =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        public OptionLabels(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            _service = service;
        }

        /// <summary>The label for one option, or the number when the option set has no such value.</summary>
        public string Label(string entityLogicalName, string attribute, int value)
        {
            var map = MapFor(entityLogicalName, attribute);
            string label;
            if (map != null && map.TryGetValue(value, out label))
            {
                return label;
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The label for an option a record may or may not carry. "(none)" for an empty
        /// choice, matching how the rest of an al_details line reads.
        /// </summary>
        public string Describe(string entityLogicalName, string attribute, OptionSetValue value)
        {
            return value == null ? "(none)" : Label(entityLogicalName, attribute, value.Value);
        }

        private Dictionary<int, string> MapFor(string entityLogicalName, string attribute)
        {
            var key = entityLogicalName + "." + attribute;
            Dictionary<int, string> map;
            if (_cache.TryGetValue(key, out map))
            {
                return map;
            }

            var response = (RetrieveAttributeResponse)_service.Execute(new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = attribute,
                RetrieveAsIfPublished = true,
            });

            // Picklist, state, status and multi-select all derive from EnumAttributeMetadata,
            // so one cast covers every choice column this is asked about. Anything else — a
            // string or a lookup reached by mistake — caches a null map and falls back to the
            // number, rather than throwing inside a command that was only annotating itself.
            var options = response.AttributeMetadata as EnumAttributeMetadata;
            if (options != null && options.OptionSet != null)
            {
                map = new Dictionary<int, string>();
                foreach (var option in options.OptionSet.Options)
                {
                    if (!option.Value.HasValue)
                    {
                        continue;
                    }

                    var text = LabelText(option.Label);
                    if (text != null)
                    {
                        map[option.Value.Value] = text;
                    }
                }
            }

            _cache[key] = map;
            return map;
        }

        /// <summary>
        /// The text of a label, preferring the caller's language.
        ///
        /// UserLocalizedLabel is what the platform fills in for the calling user and is the
        /// right answer whenever it is there. It is not always there — it depends on the
        /// request carrying a user language, which not every path does — and falling straight
        /// through to a number in that case would put the very digits this class exists to
        /// remove back on the screen. The localized set behind it says the same thing.
        /// </summary>
        private static string LabelText(Label label)
        {
            if (label == null)
            {
                return null;
            }

            if (label.UserLocalizedLabel != null && label.UserLocalizedLabel.Label != null)
            {
                return label.UserLocalizedLabel.Label;
            }

            if (label.LocalizedLabels != null)
            {
                foreach (var localized in label.LocalizedLabels)
                {
                    if (!string.IsNullOrEmpty(localized.Label))
                    {
                        return localized.Label;
                    }
                }
            }

            return null;
        }
    }
}
