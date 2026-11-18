using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// ResponseGuardPlugin sanitises al_answerrichtext, so its Update step must fire on a write
    /// that carries nothing else and its pre-image must hold the stored value. Nothing in the
    /// plug-in can tell that the step was registered without the column - the write simply
    /// never reaches it - so the solution's step files are read here.
    /// </summary>
    public class ResponseStepColumnsTests
    {
        private const string GuardUpdate = "{12d9d5b9-c0a3-f111-aaac-e4fade069307}.xml";
        private const string ProgressUpdate = "{62dd01c1-c0a3-f111-aaac-e4fade069307}.xml";
        private const string RichText = "al_answerrichtext";

        [Fact]
        public void The_guard_update_step_fires_on_and_images_the_rich_text_answer()
        {
            var step = Load(GuardUpdate);

            Assert.Equal("ResponseGuard: Update of al_response", (string)step.Attribute("Name"));
            Assert.Contains(RichText, Columns(step.Element("FilteringAttributes")));
            Assert.Contains(RichText, Columns(step.Descendants("SdkMessageProcessingStepImage").Single().Element("Attributes")));
        }

        [Fact]
        public void The_progress_update_step_does_not()
        {
            var step = Load(ProgressUpdate);

            Assert.Equal("ResponseProgress: Update of al_response", (string)step.Attribute("Name"));
            Assert.DoesNotContain(RichText, Columns(step.Element("FilteringAttributes")));
            Assert.DoesNotContain(RichText, Columns(step.Descendants("SdkMessageProcessingStepImage").Single().Element("Attributes")));
        }

        private static string[] Columns(XElement element)
        {
            Assert.NotNull(element);
            return element.Value.Split(',').Select(column => column.Trim()).ToArray();
        }

        private static XElement Load(string file)
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "SdkMessageProcessingSteps")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return XDocument.Load(Path.Combine(directory.FullName, "src", "SdkMessageProcessingSteps", file)).Root;
        }
    }
}
