using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using DotNetNuke.Security;
using DotNetNuke.Web.Api;
using System.Web.Script.Serialization;

public class MultilingualController : Custom.Hybrid.Api14
{
    // ---------------------------------------------------------------
    // GET api/Multilingual/Languages
    // Returns the primary and secondary languages registered in the
    // portal, so the caller never needs to hardcode language codes.
    // ---------------------------------------------------------------
    [HttpGet]
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.Admin)]
    [ValidateAntiForgeryToken]
    public object Languages()
    {
        var locales = DotNetNuke.Services.Localization.LocaleController.Instance
            .GetLocales(PortalSettings.PortalId);

        var primaryLang = PortalSettings.DefaultLanguage.ToLower();

        return new
        {
            primaryLanguage = primaryLang,
            allLanguages = locales.Keys.Select(k => k.ToLower()).ToList(),
            secondaryLanguages = locales.Keys
                .Select(k => k.ToLower())
                .Where(k => k != primaryLang)
                .ToList()
        };
    }

    // ---------------------------------------------------------------
    // POST api/Multilingual/Create
    // Body: { "FieldName": { "en-ca": "value", "fr-ca": "value" } }
    // Creates a new multilingual entity via the 2sxc edit UI endpoint,
    // which correctly handles independent language dimension entries.
    // ---------------------------------------------------------------
    [HttpPost]
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.Admin)]
    [ValidateAntiForgeryToken]
    public object Create()
    {
        try
        {
            var serializer = new JavaScriptSerializer();
            var data = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(
                Request.Content.ReadAsStringAsync().Result);

            var contentTypeName = "BilingualPerson";
            var contentTypeId = App.Data.GetContentType(contentTypeName).NameId;
            var newGuid = Guid.NewGuid().ToString();

            var payload = BuildSavePayload(
                data:            data,
                contentTypeName: contentTypeName,
                contentTypeId:   contentTypeId,
                guid:            newGuid,
                entityId:        null,
                isNew:           true
            );

            var response = PostToSaveEndpoint(serializer.Serialize(payload));
            return new { success = true, response = serializer.Deserialize<object>(response) };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, stackTrace = ex.StackTrace };
        }
    }

    // ---------------------------------------------------------------
    // POST api/Multilingual/Update?entityId=123
    // Body: { "FieldName": { "en-ca": "value", "fr-ca": "value" } }
    // Updates an existing multilingual entity via the 2sxc edit UI
    // endpoint, which correctly handles independent language dimensions.
    // ---------------------------------------------------------------
    [HttpPost]
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.Admin)]
    [ValidateAntiForgeryToken]
    public object Update(int entityId)
    {
        try
        {
            var serializer = new JavaScriptSerializer();
            var data = serializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(
                Request.Content.ReadAsStringAsync().Result);

            var contentTypeName = "BilingualPerson";

            var existing = App.Data.GetStream(contentTypeName).List
                .FirstOrDefault(e => e.EntityId == entityId);

            if (existing == null)
                return new { success = false, error = "Entity not found: " + entityId };

            var contentTypeId = App.Data.GetContentType(contentTypeName).NameId;

            var payload = BuildSavePayload(
                data:            data,
                contentTypeName: contentTypeName,
                contentTypeId:   contentTypeId,
                guid:            existing.EntityGuid.ToString(),
                entityId:        entityId,
                isNew:           false
            );

            var response = PostToSaveEndpoint(serializer.Serialize(payload));
            return new { success = true, id = entityId, response = serializer.Deserialize<object>(response) };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message, stackTrace = ex.StackTrace };
        }
    }

    // ---------------------------------------------------------------
    // GET api/Multilingual/List
    // Returns all BilingualPerson records for inspection.
    // ---------------------------------------------------------------
    [HttpGet]
    [DnnModuleAuthorize(AccessLevel = SecurityAccessLevel.Admin)]
    [ValidateAntiForgeryToken]
    public new object List()
    {
        var items = App.Data.GetStream("BilingualPerson").List;
        return items.Select(e => {
            var p = AsDynamic(e);
            return new
            {
                id = e.EntityId,
                guid = e.EntityGuid,
                name = (string)p.Name,
                eyeColour = p.EyeColour as string,
                favouriteFood = (string)p.FavouriteFood
            };
        });
    }

    // ---------------------------------------------------------------
    // Builds the payload in the exact format the 2sxc v21 edit UI
    // endpoint expects for both Create and Update operations.
    // ---------------------------------------------------------------
    private Dictionary<string, object> BuildSavePayload(
        Dictionary<string, Dictionary<string, object>> data,
        string contentTypeName,
        string contentTypeId,
        string guid,
        int? entityId,
        bool isNew)
    {
        // Build attributes: { "String": { "FieldName": { "en-ca": "val", "fr-ca": "val" } } }
        var attributes = new Dictionary<string, object>();
        foreach (var field in data)
        {
            var langValues = new Dictionary<string, object>();
            foreach (var lang in field.Value)
                langValues[lang.Key] = lang.Value;
            attributes[field.Key] = langValues;
        }

        var entity = new Dictionary<string, object>
        {
            { "Attributes", new Dictionary<string, object> { { "String", attributes } } },
            { "Guid", guid },
            { "Type", new Dictionary<string, object>
                {
                    { "Id", contentTypeId },
                    { "Name", contentTypeName }
                }
            },
            { "For", null },
            { "Metadata", null }
        };

        if (!isNew)
        {
            entity["Id"] = entityId;
            entity["Owner"] = "dnn:userid=" + PortalSettings.UserId;
        }

        var header = new Dictionary<string, object>
        {
            { "Guid", guid },
            { "ContentTypeName", contentTypeId },
            { "For", null },
            { "clientId", 0 },
            { "DuplicateEntity", null },
            { "Add", isNew },
            { "Index", null },
            { "EditInfo", new Dictionary<string, object> { { "ReadOnly", false } } },
            { "IsEmptyAllowed", false },
            { "IsEmpty", false },
            { "ClientData", new Dictionary<string, object>() }
        };

        if (!isNew)
            header["EntityId"] = entityId;

        return new Dictionary<string, object>
        {
            { "Items", new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "Entity", entity },
                        { "Header", header }
                    }
                }
            },
            { "IsPublished", true },
            { "DraftShouldBranch", false }
        };
    }

    // ---------------------------------------------------------------
    // Posts a JSON payload to the 2sxc v21 edit save endpoint,
    // forwarding the required auth headers from the current request.
    // Uses UTF-8 byte encoding to correctly handle accented characters.
    // ---------------------------------------------------------------
    private string PostToSaveEndpoint(string payloadJson)
    {
        var client = new System.Net.WebClient();
        client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";

        IEnumerable<string> headerValues;
        if (Request.Headers.TryGetValues("RequestVerificationToken", out headerValues))
            client.Headers["RequestVerificationToken"] = headerValues.FirstOrDefault();
        if (Request.Headers.TryGetValues("ModuleId", out headerValues))
            client.Headers["ModuleId"] = headerValues.FirstOrDefault();
        if (Request.Headers.TryGetValues("TabId", out headerValues))
            client.Headers["TabId"] = headerValues.FirstOrDefault();

        var cookieHeader = Request.Headers.GetCookies();
        if (cookieHeader.Any())
        {
            client.Headers[System.Net.HttpRequestHeader.Cookie] = string.Join("; ",
                cookieHeader.SelectMany(c => c.Cookies)
                .Select(c => c.Name + "=" + c.Value));
        }

        var saveUrl = Request.RequestUri.GetLeftPart(System.UriPartial.Authority)
            + "/api/2sxc/cms/edit/save?appId=" + App.AppId + "&partOfPage=false";

        try
        {
            var responseBytes = client.UploadData(saveUrl, "POST",
                System.Text.Encoding.UTF8.GetBytes(payloadJson));
            return System.Text.Encoding.UTF8.GetString(responseBytes);
        }
        catch (System.Net.WebException ex)
        {
            var responseBody = "";
            if (ex.Response != null)
            {
                using (var stream = ex.Response.GetResponseStream())
                using (var reader = new System.IO.StreamReader(stream))
                    responseBody = reader.ReadToEnd();
            }
            throw new Exception("HTTP "
                + (int)((System.Net.HttpWebResponse)ex.Response).StatusCode
                + " from save endpoint: " + responseBody);
        }
    }
}
