# 2sxc Multilingual Programmatic Entity Creation

A proof-of-concept 2sxc app for DNN that demonstrates how to programmatically create and update multilingual entities in **2sxc v21**, using all registered portal languages with fully independent language dimension entries.

## Background

Migrating data into a multilingual DNN/2sxc site is a common requirement. The standard `App.Data.Create()` and `App.Data.Update()` APIs only support single-language (primary language) writes. There was no documented way to programmatically create entities with values for multiple languages simultaneously.

This project documents the investigation, the approaches that failed, and the working solution.

---

## The Problem

On a multilingual DNN portal (e.g. `en-CA` + `fr-CA`), calling `App.Data.Create()` only populates primary language field values. Secondary language fields are set to "auto" (inherited from the primary), with no public API to set independent values.

This makes automated data migration impossible for multilingual sites without manual re-entry of every secondary language value through the edit UI.

---

## What Was Tried (and Failed)

### Option A — Thread Culture Trick
Temporarily switching `Thread.CurrentThread.CurrentCulture` to the target language before calling `App.Data.Update()`. **Failed** — the save pipeline reads language context from the DI-injected `DnnSite` service, not from the thread culture.

### Option B — DNN Locale Switching
Calling `DotNetNuke.Services.Localization.Localization.SetLanguage(language)` before saving. **Failed** — same reason as above.

### Option C — HttpContext Items Swap
Writing directly to `HttpContext.Current.Items["PortalSettings"]`. **Failed** — `PortalSettings.Current` is read-only and the underlying store swap had no effect on the DI-injected `DnnSite`.

### Option D — Reflection on `_defaultLanguage`
Using reflection to set the private `_defaultLanguage` field on the `DnnSite` service instance obtained via `GetService<ISite>()`. **Failed** — `App.Data.Update()` ignores this field; it reads language context from a different point in the call chain.

### Root Cause
The language dimension assignment happens inside `EntitiesManager.UpdateParts()`, deep in the EAV call chain:

```
App.Data.Update()
  → SimpleDataController.Update()
    → EntitiesManager.UpdateParts()
      → ImportExportEnvironmentBase.SaveOptions()
        → DnnSite.get_DefaultLanguage()   ← locked at DI construction time
```

`DnnSite` is constructed with a fixed `PortalSettings` instance via dependency injection at request startup. No external manipulation can change what language `SaveOptions()` sees.

---

## The Working Solution

The 2sxc edit UI itself saves multilingual data correctly — that is its entire purpose. By examining the browser Network tab during a normal edit UI save, we discovered the internal save endpoint:

```
POST /api/2sxc/cms/edit/save?appId={appId}&partOfPage=false
```

This endpoint accepts a JSON payload with multilingual field values in the format:

```json
{
  "Items": [{
    "Entity": {
      "Attributes": {
        "String": {
          "Title": { "en-ca": "English Title", "fr-ca": "French Title" },
          "Body":  { "en-ca": "English Body",  "fr-ca": "French Body" }
        }
      },
      "Guid": "new-guid-here",
      "Type": { "Id": "content-type-guid", "Name": "YourContentType" }
    },
    "Header": {
      "Guid": "new-guid-here",
      "ContentTypeName": "content-type-guid",
      "Add": true
    }
  }],
  "IsPublished": true,
  "DraftShouldBranch": false
}
```

A custom WebApi controller calls this endpoint server-side, forwarding the authentication headers from the original request. This bypasses `App.Data.Create/Update` entirely and uses the same pipeline the edit UI uses, which correctly creates independent language dimension entries for each language.

**Key implementation detail:** The payload must be sent as UTF-8 encoded bytes (not as a string) to correctly handle accented characters in languages like French, German, etc.

---

## App Structure

```
Multilingual POC/
├── app.json                        ← 2sxc app manifest
├── app.csproj                      ← VS Code IntelliSense configuration
├── _TestPage.cshtml                ← Test UI with buttons for each endpoint
└── api/
    ├── MultilingualController.cs   ← WebApi controller with Create/Update/List/Languages
    └── web.config                  ← Assembly references for runtime compilation
```

---

## API Endpoints

### GET `api/Multilingual/Languages`
Returns all languages registered in the portal.

```json
{
  "primaryLanguage": "en-ca",
  "allLanguages": ["en-ca", "fr-ca"],
  "secondaryLanguages": ["fr-ca"]
}
```

### POST `api/Multilingual/Create`
Creates a new multilingual entity. Body format:

```json
{
  "Name":          { "en-ca": "John Smith",     "fr-ca": "Jean Dupont" },
  "EyeColour":     { "en-ca": "Blue",           "fr-ca": "Bleu" },
  "FavouriteFood": { "en-ca": "Chocolate cake", "fr-ca": "Gâteau au chocolat" }
}
```

### POST `api/Multilingual/Update?entityId=123`
Updates an existing entity. Same body format as Create.

### GET `api/Multilingual/List`
Returns all BilingualPerson records with values for the current page language.

---

## Setup Instructions

### Prerequisites
- DNN 9.11+ with 2sxc v21 installed
- A multilingual portal with at least two languages registered

### Steps

1. **Install the app** — copy the app folder to your portal's 2sxc directory or import as a 2sxc app ZIP

2. **Create the content type** — in the 2sxc admin for this app, create a content type called `BilingualPerson` with these three string fields:
   - `Name`
   - `EyeColour`
   - `FavouriteFood`

3. **Configure `app.csproj`** — update the `PathBin` value to point to your DNN `bin` folder if it differs from the default:
   ```xml
   <PathBin>..\..\..\..\bin</PathBin>
   ```

4. **Add the module to a page** — add the Multilingual POC module to a DNN page and select `_TestPage.cshtml` as the view

5. **Test** — use the buttons on the test page to verify each endpoint works correctly

### Adapting for Your Own Content Type

To use this pattern with your own content type, change `"BilingualPerson"` to your content type name in three places in `MultilingualController.cs`:

- `Create()` — `var contentTypeName = "BilingualPerson";`
- `Update()` — `var contentTypeName = "BilingualPerson";`
- `List()` — `App.Data.GetStream("BilingualPerson")`

Then adjust the field names in `List()` to match your content type's fields.

---

## VS Code / IntelliSense Notes

The `app.csproj` file configures VS Code IntelliSense for this project. It references assemblies from the DNN `bin` folder. If VS Code shows errors, verify that `PathBin` in `app.csproj` correctly points to your DNN installation's `bin` folder.

The `api/web.config` file provides assembly references required at **runtime** by 2sxc's Razor compiler:
- `System.Web.Extensions` — provides `JavaScriptSerializer`
- `System.Net.Http` — provides `HttpRequestMessage` and `GetCookies()`

These are standard .NET Framework 4.7.2 assemblies and do not require any additional installation.

---

## Implications and Future Work

This proof of concept demonstrates that multilingual programmatic entity creation **is possible** in 2sxc v21, but requires calling an internal endpoint rather than the public `App.Data` API.

The ideal solution would be for the 2sxc team to expose this capability through the public API, for example:

```csharp
// Proposed API - does not exist yet
App.Data.Create("ContentType", new Dictionary<string, Dictionary<string, object>>
{
    ["Title"] = new Dictionary<string, object>
    {
        ["en-ca"] = "English Title",
        ["fr-ca"] = "French Title"
    }
});
```

A feature request has been raised with the 2sxc team. This repository serves as the reference implementation to support that request.

---

## Tested With

- DNN 9.13.10
- 2sxc 21.05.00
- Portal languages: `en-CA` (primary) + `fr-CA` (secondary)

---

## License

MIT — free to use, adapt, and distribute.
