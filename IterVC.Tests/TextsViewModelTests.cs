using IterVC.Core.Localization;
using IterVC.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class TextsViewModelTests
{
    [TestMethod]
    public void RaiseAll_NotifiesNoiseGateAdvancedOnceAndUsesDedicatedCaptureErrorKey()
    {
        var original = LocalizationService.Instance.CurrentLanguage;
        try
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguages.Spanish);
            var texts = new TextsViewModel();
            var notifications = new List<string>();
            texts.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is not null) notifications.Add(args.PropertyName);
            };

            LocalizationService.Instance.SetLanguage(SupportedLanguages.English);
            texts.RaiseAll();

            Assert.AreEqual(1, notifications.Count(name => name == nameof(TextsViewModel.NoiseGateAdvanced)));
            Assert.AreEqual(LocalizationService.Instance.Get(LocalizationService.Keys.AppCaptureError),
                texts.AppCaptureError);
            Assert.AreNotEqual(LocalizationService.Instance.Get(LocalizationService.Keys.ButtonStart) + ":",
                texts.AppCaptureError);
        }
        finally { LocalizationService.Instance.SetLanguage(original); }
    }
}
