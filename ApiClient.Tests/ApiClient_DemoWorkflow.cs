using Xunit;
using ConstataGraphQL;
using System.Text.Json;

namespace ConstataGraphQl.UnitTests {
  public class ApiClient_DemoWorkflows {
    // This is your constata encrypted_key, found in signature.json
    // This is the development encrypted key.
    static string encrypted_key = "e002ccd7fe2357f72e700dbe07d127304400000000000000b4c9b15aa8d32e22e579fedf254cc5429d925c9eac20698faea5b937abcb66a46489285b646e69ebfb425f6799e9ea2441f51be393c53012591ee8596c1e215747feb838";

    // Password should live only in this process memory. Can be sourced from stdin when the process starts, from a keyring, using systemd-ask-password in linux.
    static string password = "password";

    static string environment = "staging"; // "development"; // "production";

    [Fact]
    public async Task CreatesAttestation() {
      // This throws if decryption fails, likely to password being wrong.
      // The last boolean param means "testing", set it to false (or remove it) to connect to https://api.constata.eu
      var client = new ApiClient(encrypted_key, password, environment);

      // If you want to receive web callbacks when your attestation is done being certified, set up a web callbacks url.
      // We will POST a web callback signed by constata to this url. See the web callbacks management example below.
      // You should do this just once, to set your web callbacks url.
      await client.updateWebCallbacksUrl("https://enoiywcgawrvo5j.m.pipedream.net");
      
      byte[][] files = {File.ReadAllBytes("../../../music.mp3"), File.ReadAllBytes("../../../lyrics.txt") };

      var attestation = await client.createAttestation(files, new string[]{"foo@example.com", "bar@example.com"},  "organ sample");
      Assert.NotNull(attestation);
      Console.WriteLine("Created attestation: {0}", JsonSerializer.Serialize(attestation));
      Assert.Equal("processing", attestation.State);

      // Atestation's State should be "processing".
      // If it's parked, it can be either because you need to buy tokens or you need to accept constata's terms and conditions.
      // You'll see the parking_reason available in the attestation.
      // You'll also find urls to accept the TyC or buy tokens, which you should visit from your browser. 
      // You can refetch an attestation this way:
      attestation = await client.Attestation(attestation.Id);
      Console.WriteLine("Reloaded attestation {0}, state is: {1}", attestation.Id, attestation.State);

      // You can perform arbitrary GraphQL queries and get responses as object or custom types.
      // See ApiClient.cs for other examples.
      // More info is available at https://github.com/graphql-dotnet/graphql-client
      var account_response = await client.Query<AccountStateResponse>(
        "AccountState",
        @"
        query AccountState($id: Int!) {
          AccountState(id: $id) {
            id
            missing
            tokenBalance
          }
        }",
        new { id = 1 }
      );

      Console.WriteLine("Your account status is: {0}", JsonSerializer.Serialize(account_response));
    }

    public class AccountStateResponse {
      public AccountStateContent? AccountState { get; set; }

      public class AccountStateContent {
        public int Id { get; set; }
        public int TokenBalance { get; set; }
        public int Missing { get; set; }
        // An AccountState has some more fields.
      }
    }

    [Fact]
    public async Task FindsDoneAttestations() {
      var client = new ApiClient(encrypted_key, password, environment); // This throws if decryption fails, likely to password being wrong.
      
      // This method returns up to 200 attestations sorted by creation date descending. Argument is the page number, 0 based.
      // Advanced filtering, ordering and pagination are available as low level GraphQL calls,
      // but we suggest you store the Attestation Id and metadata in your DB.
      var attestations = await client.allAttestations(0);

      foreach (Attestation attestation in attestations) {
        if (attestation.State != "done") {
          Console.WriteLine("Attestation {0} is not done, it's {1}", attestation.Id, attestation.State);
          continue;
        }

        Console.WriteLine("Attestation {0} is done. Visit {1} to view, share and download.", attestation.Id, attestation.AdminAccessUrl);

        /*
        Console.WriteLine("Also writing HTML proof to proof_{0}.html, you can open it in your browser.", attestation.Id);
        var export = await client.AttestationHtmlExport(1);
        File.WriteAllText($"../../../proof_{attestation.Id}.html", export.VerifiableHtml);
        */
      }
    }

    [Fact]
    public async Task ListsWebCallbacks() {
      // This throws if decryption fails, likely to password being wrong.
      // The last boolean param means "testing", set it to false (or remove it) to connect to https://api.constata.eu
      var client = new ApiClient(encrypted_key, password, environment);
      
      // The WebCallback queries help you debug your web callbacks.
      var webCallbacks = await client.allWebCallbacks(0);

      // You can set your web callbacks URL once, and you can change it whenever you want.
      // If an Attestation is Done, and you don't have a web callbacks url set, we will never send a callback for it.
      // If you had a web callbacks url set when the Attestation was done, and then you change it, all attempts will be sent to the new one.
      //await client.updateWebCallbacksUrl("https://127.0.0.1:8000/constata_web_callbacks");

      foreach (WebCallback webCallback in webCallbacks) {
        Console.WriteLine("WebCallback {0} is in state {1}", webCallback.Id, webCallback.State);

        // This is how you would validate the received body in your handler, to make sure it's correctly signed.
        // Your handler should also return an HTTP 200 status on success, with any body you like.
        // If you ever need to sync callbacks on a background job manually, you can feed the RequestBody to your routine.
        // New kinds of callbacks may be added in the future, so make sure you check they are for AttestationDone before processing them.
        var parsed = new ParsedWebCallback(webCallback.RequestBody, environment);
        Console.WriteLine("The callback is of kind {0}, for the resource with id {1}, and the admin url to share with the recipient is {2}",
          parsed.Kind,
          parsed.Resource["AttestationDone"]!["id"]!.GetValue<int>(),
          parsed.Resource["AttestationDone"]!["admin_access_url"]!.GetValue<string>()
        );
      }
    }
  }
}
