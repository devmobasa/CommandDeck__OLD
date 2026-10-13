using System.Text.Json;
using CommandDeck.Extensions.Testing;
using CommandDeck.Extensions.Views;
using CommandDeck.ExtensionSample;

await using var host = await ExtensionTestHost.StartAsync(new DraftCommand(), new()
{
    Settings = new() { ["defaultTitle"] = JsonElement.Parse("\"Example\"") }
});
await host.OpenAsync();
if (host.CurrentView is not FormView { Title: "Local draft" }) {
throw new InvalidOperationException("Draft form did not open.");
}
var values = new Dictionary<string, JsonElement>
{
    ["title"] = JsonElement.Parse("\"Untitled\""), ["body"] = JsonElement.Parse("\"Body\""),
    ["category"] = JsonElement.Parse("\"note\""), ["done"] = JsonElement.Parse("false")
};
await host.SubmitAsync(values);
if (host.CurrentView is not FormView { FieldErrors.Count: 1, ValidatedFormRevision: 1 }) {
throw new InvalidOperationException("Expected title validation.");
}
values["title"] = JsonElement.Parse("\"Saved example\"");
await host.SubmitAsync(values);
if (host.CurrentView is not FormView { Title: "Draft saved", FieldErrors.Count: 0, ValidatedFormRevision: 2 }) {
throw new InvalidOperationException("Expected successful save.");
}
Console.WriteLine("PASS LocalDraft real SDK opens, validates and saves through the author test kit");
