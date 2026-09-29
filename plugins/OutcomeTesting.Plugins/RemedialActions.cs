using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The checker's remedial actions (project owner, 2026-09-29): what the adviser is to do
    /// about each thing the checker marked down, written by the CHECKER before the check is
    /// submitted. Before this the adviser wrote the remedial action themselves, in
    /// <c>al_adviserresponse</c>, after the remediation reached them (AD-095).
    ///
    /// The actions do not exist until the submit raises them, so the words are parked on the
    /// review in <see cref="PendingAttr"/> - the same shape "Who carries this fail" uses for
    /// <c>al_pendingaccountability</c> - keyed by each item's text exactly as
    /// <see cref="Remediation.NonPassItems"/> produces it. That text is the only thing the
    /// page and the server both know about an item before any action exists. A review with
    /// nothing itemised owes one action for the whole check, keyed <see cref="OverallKey"/>.
    ///
    /// Stored as an array of entries rather than a JSON object because
    /// DataContractJsonSerializer writes a dictionary as key/value pairs, and an array is a
    /// shape the page can write without knowing that.
    ///
    /// The submit gate, and anything else that needs <see cref="Remediation"/>, lives in
    /// RemedialActionsSubmitGate.cs, so the Registration tool can link this file on its own.
    /// </summary>
    public static partial class RemedialActions
    {
        /// <summary>The single action a check with no itemised fail point owes.</summary>
        public const string OverallKey = "__overall__";

        /// <summary><c>al_reviewinstance</c>: the parked map, cleared once its actions are raised.</summary>
        public const string PendingAttr = "al_pendingremedialactions";

        /// <summary><c>al_remediationaction</c>: the checker's words. Written once, on Create.</summary>
        public const string ActionAttr = "al_remedialaction";

        /// <summary><c>al_remediationaction</c>: the adviser's Yes / No.</summary>
        public const string ActionPerformedAttr = "al_actionperformed";

        public const int ActionPerformedYes = 120910815;
        public const int ActionPerformedNo = 120910816;

        /// <summary>The length of <see cref="ActionAttr"/>, and the most one entry may carry.</summary>
        public const int MaxLength = 4000;

        /// <summary>The stored map, or an empty one when there is nothing readable.</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json))
            {
                return map;
            }

            List<RemedialActionEntry> entries;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    entries = new DataContractJsonSerializer(typeof(List<RemedialActionEntry>))
                        .ReadObject(stream) as List<RemedialActionEntry>;
                }
            }
            catch (SerializationException)
            {
                return map;
            }
            catch (InvalidCastException)
            {
                return map;
            }

            if (entries == null)
            {
                return map;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var key = (entry.Item ?? string.Empty).Trim();
                var text = (entry.Text ?? string.Empty).Trim();
                if (key.Length > 0 && text.Length > 0)
                {
                    map[key] = text;
                }
            }

            return map;
        }

        /// <summary>
        /// The map as stored, with blank entries dropped. Null when nothing is left, so the
        /// column reads as empty rather than as an empty array.
        /// </summary>
        public static string Serialise(IDictionary<string, string> map)
        {
            var entries = new List<RemedialActionEntry>();
            if (map != null)
            {
                foreach (var pair in map)
                {
                    var key = (pair.Key ?? string.Empty).Trim();
                    var text = (pair.Value ?? string.Empty).Trim();
                    if (key.Length > 0 && text.Length > 0)
                    {
                        entries.Add(new RemedialActionEntry { Item = key, Text = text });
                    }
                }
            }

            if (entries.Count == 0)
            {
                return null;
            }

            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(List<RemedialActionEntry>)).WriteObject(stream, entries);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>The words parked for one item, trimmed, or null when there are none.</summary>
        public static string TextFor(IDictionary<string, string> map, string item)
        {
            if (map == null || item == null)
            {
                return null;
            }

            string text;
            if (!map.TryGetValue(item.Trim(), out text) || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text.Trim();
        }

        /// <summary>Each item's words, in item order, with null where there are none.</summary>
        public static List<string> Align(IList<string> items, IDictionary<string, string> map)
        {
            var texts = new List<string>();
            if (items == null)
            {
                return texts;
            }

            foreach (var item in items)
            {
                texts.Add(TextFor(map, item));
            }

            return texts;
        }

        /// <summary>
        /// Why this review cannot be submitted yet, or null when every item it will raise has
        /// its words. Keys that are no longer items are ignored: they are fail points the
        /// checker unticked, kept so that ticking one again brings the words back.
        /// </summary>
        public static string Refusal(IList<string> items, IDictionary<string, string> map)
        {
            var listed = new List<string>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        listed.Add(item.Trim());
                    }
                }
            }

            if (listed.Count == 0)
            {
                return TextFor(map, OverallKey) != null
                    ? null
                    : "Write the overall remedial action under 'Fail points and remedial actions' before "
                        + "submitting. This check owes a remediation, and what you write there is what the "
                        + "adviser is asked to do.";
            }

            foreach (var item in listed)
            {
                if (TextFor(map, item) == null)
                {
                    return "Write the remedial action for '" + item + "' under 'Fail points and remedial "
                        + "actions' before submitting.";
                }
            }

            return null;
        }

        /// <summary>The map parked on a review.</summary>
        public static Dictionary<string, string> Pending(IOrganizationService service, Guid reviewId)
        {
            var review = service.Retrieve("al_reviewinstance", reviewId, new ColumnSet(PendingAttr));
            return Parse(review.GetAttributeValue<string>(PendingAttr));
        }

        /// <summary>
        /// The Yes / No the form prints, without a metadata read. Two values, fixed by this
        /// solution, so the label is known here the way the portal and the Code App know it.
        /// </summary>
        public static string ActionPerformedLabel(int? value)
        {
            if (value == ActionPerformedYes)
            {
                return "Yes";
            }

            return value == ActionPerformedNo ? "No" : null;
        }
    }

    /// <summary>One parked remedial action: the item it answers and the checker's words.</summary>
    [DataContract]
    public sealed class RemedialActionEntry
    {
        [DataMember(Name = "item")]
        public string Item { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_remedialactionsrequest</c>: the review the
    /// words belong to and every entry the page holds for it.
    /// </summary>
    [DataContract]
    public sealed class RemedialActionsRequestPayload
    {
        [DataMember(Name = "reviewId")]
        public string ReviewId { get; set; }

        [DataMember(Name = "actions")]
        public List<RemedialActionEntry> Actions { get; set; }

        public static RemedialActionsRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return new DataContractJsonSerializer(typeof(RemedialActionsRequestPayload))
                        .ReadObject(stream) as RemedialActionsRequestPayload;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }
}
