using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Loads data/v8-seed/data.xml - the Checker Checklist as the environments hold it - into a
    /// fake organisation service, so a test of the drawn form draws the real form rather than
    /// three questions a test invented.
    /// </summary>
    public static class V8Seed
    {
        public static readonly Guid ChecklistVersionId = Guid.Parse("22220000-0000-4c00-8000-000000000008");

        private static readonly string[] OptionSets = { "al_ownerrole", "al_responsetype", "al_category" };
        private static readonly string[] Flags = { "al_isconditional", "al_ismandatory", "al_isoptional" };
        private static readonly string[] Numbers = { "al_displayorder", "al_versionnumber" };
        private static readonly string[] Dates = { "al_effectivefrom", "al_effectiveto" };

        public static void Load(FakeOrganizationService service)
        {
            var document = XDocument.Load(Path());
            foreach (var entity in document.Root.Elements("entity"))
            {
                var logicalName = (string)entity.Attribute("name");
                foreach (var record in entity.Element("records").Elements("record"))
                {
                    var row = service.Seed(logicalName, Guid.Parse((string)record.Attribute("id")));
                    foreach (var field in record.Elements("field"))
                    {
                        var name = (string)field.Attribute("name");
                        var value = (string)field.Attribute("value");
                        var lookup = (string)field.Attribute("lookupentity");

                        if (lookup != null)
                        {
                            row[name] = new EntityReference(lookup, Guid.Parse(value))
                            {
                                Name = (string)field.Attribute("lookupentityname"),
                            };
                        }
                        else if (Array.IndexOf(OptionSets, name) >= 0)
                        {
                            row[name] = new OptionSetValue(int.Parse(value, CultureInfo.InvariantCulture));
                        }
                        else if (Array.IndexOf(Flags, name) >= 0)
                        {
                            row[name] = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (Array.IndexOf(Numbers, name) >= 0)
                        {
                            row[name] = int.Parse(value, CultureInfo.InvariantCulture);
                        }
                        else if (Array.IndexOf(Dates, name) >= 0)
                        {
                            row[name] = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                        }
                        else
                        {
                            row[name] = value;
                        }
                    }
                }
            }
        }

        /// <summary>The question version in force for a question code, as seeded.</summary>
        public static Guid VersionOf(FakeOrganizationService service, string questionCode, DateTime asOf)
        {
            Guid questionId = Guid.Empty;
            foreach (var question in service.All("al_question"))
            {
                if ((string)question["al_questioncode"] == questionCode)
                {
                    questionId = question.Id;
                }
            }

            foreach (var version in service.All("al_questionversion"))
            {
                var parent = version.GetAttributeValue<EntityReference>("al_questionid");
                if (parent != null && parent.Id == questionId
                    && ResponseRules.IsVersionEffective(
                        version.GetAttributeValue<DateTime?>("al_effectivefrom"),
                        version.GetAttributeValue<DateTime?>("al_effectiveto"),
                        asOf))
                {
                    return version.Id;
                }
            }

            throw new InvalidOperationException("No version of " + questionCode + " is in force.");
        }

        private static string Path()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var candidate = System.IO.Path.Combine(directory.FullName, "data", "v8-seed", "data.xml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("data/v8-seed/data.xml was not found above the test directory.");
        }
    }
}
