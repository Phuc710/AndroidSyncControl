using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace AndroidSyncControl.Tests
{
    public class LocalizationTests
    {
        private static readonly XNamespace SysNs = "clr-namespace:System;assembly=System.Runtime";
        private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private string FindLocalizationFile(string fileName)
        {
            string current = Directory.GetCurrentDirectory();
            while (current != null && !File.Exists(Path.Combine(current, "AndroidSyncControl.sln")))
            {
                current = Directory.GetParent(current)?.FullName;
            }

            Assert.NotNull(current);
            string filePath = Path.Combine(current, "src", "AndroidSyncControl", "Localization", fileName);
            Assert.True(File.Exists(filePath), $"File not found: {filePath}");
            return filePath;
        }

        [Fact]
        public void Localization_EnglishAndVietnamese_HaveIdenticalKeys()
        {
            string enPath = FindLocalizationFile("Strings.en.xaml");
            string viPath = FindLocalizationFile("Strings.vi.xaml");

            var enDoc = XDocument.Load(enPath);
            var viDoc = XDocument.Load(viPath);

            var enKeys = enDoc.Descendants(SysNs + "String")
                .Select(e => e.Attribute(XNs + "Key")?.Value)
                .Where(k => !string.IsNullOrEmpty(k))
                .OrderBy(k => k)
                .ToList();

            var viKeys = viDoc.Descendants(SysNs + "String")
                .Select(e => e.Attribute(XNs + "Key")?.Value)
                .Where(k => !string.IsNullOrEmpty(k))
                .OrderBy(k => k)
                .ToList();

            Assert.NotEmpty(enKeys);
            Assert.NotEmpty(viKeys);

            // Verify LanguageManager marker key exists in both
            Assert.Contains("Str.Btn.About", enKeys);
            Assert.Contains("Str.Btn.About", viKeys);

            // Verify keys match 1:1
            Assert.Equal(enKeys, viKeys);
        }

        [Theory]
        [InlineData("Str.Connect.Title")]
        [InlineData("Str.Connect.InitDetail")]
        [InlineData("Str.Connect.Initializing")]
        [InlineData("Str.Connect.Connecting")]
        [InlineData("Str.Connect.AdbUnavailable")]
        [InlineData("Str.Connect.AdbError")]
        [InlineData("Str.Connect.NoDevice")]
        [InlineData("Str.Action.Info")]
        [InlineData("Str.Action.Proxy")]
        [InlineData("Str.Action.BypassShopee")]
        [InlineData("Str.Update.Title")]
        [InlineData("Str.Update.Available")]
        [InlineData("Str.Status.Ready")]
        [InlineData("Str.Status.Pasting")]
        [InlineData("Str.Status.Capturing")]
        [InlineData("Str.Status.Rebooting")]
        [InlineData("Str.Status.Installing")]
        [InlineData("Str.Status.OpeningShopee")]
        [InlineData("Str.Status.RotatingIp")]
        [InlineData("Str.Status.ProxyCleared")]
        [InlineData("Str.Dialog.RebootConfirm")]
        [InlineData("Str.Dialog.ProxyEmptyPrompt")]
        [InlineData("Str.Dialog.ProxyCurrentPrompt")]
        [InlineData("Str.Dialog.Apply")]
        [InlineData("Str.Bypass.Step1")]
        [InlineData("Str.Bypass.Done")]
        [InlineData("Str.Nav.Recent")]
        public void Localization_CoreKeys_ExistInBothDictionaries(string key)
        {
            string enPath = FindLocalizationFile("Strings.en.xaml");
            string viPath = FindLocalizationFile("Strings.vi.xaml");

            var enDoc = XDocument.Load(enPath);
            var viDoc = XDocument.Load(viPath);

            bool inEn = enDoc.Descendants(SysNs + "String").Any(e => e.Attribute(XNs + "Key")?.Value == key);
            bool inVi = viDoc.Descendants(SysNs + "String").Any(e => e.Attribute(XNs + "Key")?.Value == key);

            Assert.True(inEn, $"Key '{key}' missing from Strings.en.xaml");
            Assert.True(inVi, $"Key '{key}' missing from Strings.vi.xaml");
        }
    }
}
