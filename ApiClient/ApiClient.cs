extern alias RealNBitcoin;

using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Abstractions;
using GraphQL.Client.Serializer.Newtonsoft;
using System.Security.Cryptography;
using Miscreant;
using NBitcoin;
using Blockcore.Networks;
using Blockcore.Networks.Bitcoin;
using RNB = RealNBitcoin::NBitcoin;

namespace ConstataGraphQL {
  public class ApiClient {
    private readonly Signer Signer;
    private readonly GraphQLHttpClient Client;

    // The ApiClient is the main entry point to our API.
    // It performs authenticated GraphQL requests.
    // It supports any graphql query but has convenience functions and types for most common use cases.
    public ApiClient(string encryptedKeyText, string password, string env = "production") {
      (string url, Network signing_network) = env switch {
        "development" => ("http://127.0.0.1:8000/graphql", Networks.Bitcoin.Regtest()),
        "staging" => ("https://api-staging.constata.eu/graphql", Networks.Bitcoin.Mainnet()),
        "production" => ("https://api.constata.eu/graphql", Networks.Bitcoin.Mainnet()),
      };

      this.Client = new GraphQLHttpClient(url, new NewtonsoftJsonSerializer());
      this.Signer = new Signer(encryptedKeyText, password, signing_network);
    }

    public async Task<GraphQL.GraphQLResponse<T>> Query<T>(string operationName, string query, object variables) {
      var request = new Request(this.Signer, operationName, query, variables);
      return await this.Client.SendQueryAsync<T>(request);
    }

    public async Task<Attestation?> createAttestation(byte[][] files, string[] emailAdminAccessUrlTo, string? markers) {
      var input = new {
        documents = files.Select((f) => this.Signer.Sign(f) ),
        emailAdminAccessUrlTo = emailAdminAccessUrlTo,
        markers = markers,
      };
      
      var response = await this.Query<CreateAttestationResponse>(
        "createAttestation",
        $@"
        mutation createAttestation($input: AttestationInput!) {{
          createAttestation(input: $input) {{
            {ATTESTATION_FIELDS}
          }}
        }}",
        new { input = input }
      );

      return dataOrThrow(response).createAttestation;
    }

    public async Task<List<Attestation>?> allAttestations(int page) {
      var response = await this.Query<AttestationsResponse>(
        "myAttestationsQuery",
        $@"
        query myAttestationsQuery($page: Int) {{
          allAttestations(page: $page, perPage: 200, sortField: ""createdAt"", sortOrder: ""desc"") {{
            {ATTESTATION_FIELDS}
          }}
        }}",
        new { page = page }
      );

      return dataOrThrow(response).allAttestations;
    }

    public async Task<Attestation?> Attestation(int id) {
      var response = await this.Query<AttestationResponse>(
        "Attestation",
        $@"
        query Attestation($id: Int!) {{
          Attestation(id: $id) {{
            {ATTESTATION_FIELDS}
          }}
        }}",
        new { id = id }
      );

      return dataOrThrow(response).Attestation;
    }

    public async Task<AttestationHtmlExport?> AttestationHtmlExport(int id) {
      var response = await this.Query<AttestationHtmlExportResponse>(
        "AttestationHtmlExport",
        @"
        query AttestationHtmlExport($id: Int!) {
          AttestationHtmlExport(id: $id) {
            id
            verifiableHtml
          }
        }",
        new { id = id }
      );

      return dataOrThrow(response).AttestationHtmlExport;
    }

    public async Task<String> updateWebCallbacksUrl(string url) {
      var response = await this.Query<UpdateWebCallbacksUrlResponse>(
        "updateWebCallbacksUrl",
        @"
        mutation updateWebCallbacksUrl($url: String) {
          updateWebCallbacksUrl(url: $url) {
            id
            webCallbacksUrl
            __typename
          }
        }",
        new { url = url }
      );

      return dataOrThrow(response).updateWebCallbacksUrl.WebCallbacksUrl;
    }

    public async Task<List<WebCallback>?> allWebCallbacks(int page) {
      var response = await this.Query<WebCallbacksResponse>(
        "allWebCallbacks",
        @"
        query allWebCallbacks($page: Int) {
          allWebCallbacks(page: $page, perPage: 200, sortField: ""createdAt"", sortOrder: ""desc"") {
            id
            kind
            resourceId
            state
            lastAttemptId
            createdAt
            nextAttemptOn
            requestBody
          }
        }",
        new { page = page }
      );

      return dataOrThrow(response).allWebCallbacks;
    }

    public async Task<List<WebCallbackAttempt>?> allWebCallbackAttempts(int webCallbackId) {
      var response = await this.Query<WebCallbackAttemptsResponse>(
        "allWebCallbackAttempts",
        @"
        query allWebCallbackAttempts($filter: WebCallbackAttemptFilter) {
          allWebCallbackAttempts(page: 0, perPage: 200, sortField: ""attemptedAt"", sortOrder: ""desc"", filter: $filter) {
            id
            webCallbackId
            attemptedAt
            url
            resultCode
            resultText
          }
        }",
        new {
          filter = new {
            webCallbackIdEq = webCallbackId
          }
        }
      );

      return dataOrThrow(response).allWebCallbackAttempts;
    }

    private T dataOrThrow<T>(GraphQLResponse<T> response) {
      if (response.Errors is not null) {
        throw new Exception(string.Join("\n", response.Errors.Select((e) => e.Message)));
      }
      if (response.Data is null) {
        throw new Exception("Invalid GraphQL response: no Errors nor Data");
      }
      return response.Data;
    }

    const string ATTESTATION_FIELDS = @"
      id
      personId
      orgId
      markers
      openUntil
      state
      parkingReason
      doneDocuments
      parkedDocuments
      processingDocuments
      totalDocuments
      tokensCost
      tokensPaid
      tokensOwed
      buyTokensUrl
      acceptTycUrl
      lastDocDate
      emailAdminAccessUrlTo
      adminAccessUrl
      createdAt
      __typename
    ";
  }

  class AttestationsResponse {
    public List<Attestation>? allAttestations { get; set; }
  }

  class AttestationResponse {
    public Attestation? Attestation { get; set; }
  }

  class CreateAttestationResponse {
    public Attestation? createAttestation { get; set; }
  }

  class AttestationHtmlExportResponse {
    public AttestationHtmlExport? AttestationHtmlExport { get; set; }
  }

  public class AttestationHtmlExport {
    public int Id { get; set; }
    public string VerifiableHtml { get; set; }
  }

  public class Attestation {
    public int Id { get; set; }
    public int PersonId { get; set; }
    public string? Markers { get; set; }
    public DateTime? OpenUntil { get; set; }
    public string? State { get; set; }
    public string? ParkingReason { get; set; }
    public int DoneDocuments { get; set; }
    public int ParkedDocuments { get; set; }
    public int ProcessingDocuments { get; set; }
    public int TotalDocuments { get; set; }
    public double TokensCost { get; set; }
    public double TokensPaid { get; set; }
    public double TokensOwed { get; set; }
    public string? BuyTokensUrl { get; set; }
    public string? AcceptTycUrl { get; set; }
    public DateTime? LastDocDate { get; set; }
    public List<string>? EmailAdminAccessUrlTo { get; set; }
    public string? AdminAccessUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
  }

  public class UpdateWebCallbacksUrlResponse {
    public AccountStateContent? updateWebCallbacksUrl { get; set; }

    public class AccountStateContent {
      public int Id { get; set; }
      public string WebCallbacksUrl { get; set; }
    }
  }

  class WebCallbacksResponse {
    public List<WebCallback>? allWebCallbacks { get; set; }
  }

  public class WebCallback {
    public int Id { get; set; }
    public string Kind { get; set; }
    public int ResourceId { get; set; }
    public string State { get; set; }
    public int? LastAttemptId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptOn { get; set; }
    public string RequestBody { get; set; }
  }

  class WebCallbackAttemptsResponse {
    public List<WebCallbackAttempt>? allWebCallbackAttempts { get; set; }
  }

  public class WebCallbackAttempt {
    public int Id { get; set; }
    public int WebCallbackId { get; set; }
    public DateTime AttemptedAt{ get; set; }
    public string Url { get; set; }
    public string ResultCode { get; set; }
    public string ResultText { get; set; }
  }

  // The Signer decodes an encryptedKey with a password, and can then use it to 'Sign' byte payloads, like headers or new documents.
  public class Signer {
    private readonly Key Key;
    private readonly string Address;

    public Signer(string encryptedKeyText, string password, Network network) {
      if(password.Length > 32) {
        throw new Exception("Invalid password length");
      }

      byte[] aead_key = Encoding.ASCII.GetBytes(password);
      Array.Resize(ref aead_key, 32);

      byte[] encrypted_key = Convert.FromHexString(encryptedKeyText);

      byte[] ciphertext = encrypted_key.Skip(24).ToArray();
      byte[] nonce = encrypted_key.Take(16).ToArray();

      using (var aead = Aead.CreateAesCmacSiv(aead_key)) {
        byte[] bytes = aead.Open(ciphertext, nonce, new byte[]{});
        var decrypted_key = Encoding.ASCII.GetString(bytes);
        var key = Key.Parse(decrypted_key, network);
        var address = key.PubKey.GetAddress(network);
        this.Key = key;
        this.Address = address.ToString();
      }
    }

    public SignedPayload Sign(byte[] bytes) {
      return new SignedPayload(System.Convert.ToBase64String(bytes), this.Address, this.Key.SignMessage(bytes));
    }
  }

  public readonly struct SignedPayload {
    public SignedPayload(string payload, string signer, string signature) {
      this.payload = payload;
      this.signer = signer;
      this.signature = signature;
    }
    public string payload { get; init; }
    public string signer { get; init; }
    public string signature { get; init; }
  }

  // This custom Request builds an authentication header with signed metadata about the request itself.
  public class Request : GraphQLHttpRequest {
    private readonly Signer Signer;

    public Request(Signer signer, string operationName, string query, object variables) {
      this.Signer = signer;
      this.Query = query;
      this.OperationName = operationName;
      this.Variables = variables;
    }

    public override HttpRequestMessage ToHttpRequestMessage(GraphQLHttpClientOptions options, IGraphQLJsonSerializer serializer) {
      var r = base.ToHttpRequestMessage(options, serializer);

      byte[] requestMeta = JsonSerializer.SerializeToUtf8Bytes(new {
        path = r.RequestUri?.AbsolutePath,
        method = r.Method.Method,
        nonce = (Int64)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds,
        body_hash = (r.Content is not null) ? HexDigest(r.Content.ReadAsStringAsync().Result) : null,
        query_hash = (r.RequestUri?.Query.Length > 1) ? HexDigest(r.RequestUri.Query.Substring(1)) : null,
      });

      string header = JsonSerializer.Serialize(this.Signer.Sign(requestMeta));

      r.Headers.TryAddWithoutValidation("Authentication", header); 

      return r;
    }

    private string HexDigest(string str) {
      return Convert.ToHexString(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(str)));
    }
  }

  // The ParsedWebCallback validates a callback's signature and extracts the inner attributes you can use.
  public class ParsedWebCallback : GraphQLHttpRequest {
    public string Kind { get; init; }
    public JsonNode Resource { get; init; }

    public ParsedWebCallback(string rawBody, string env = "production") {
      (string constata_addr, RNB.Network constata_addr_network) = env switch {
        "development" => ("bcrt1qsj2h8ernt4amc674l60vu925flvn57ff9lyry2", RNB.Network.RegTest),
        "staging" => ("tb1qurghvhp8g6he5hsv0en6n59rextfw8kw0wxyun", RNB.Network.TestNet),
        "production" => ("bc1qw3ca5pgepg6hqqle2eq8qakejl5wdafs7up0jd", RNB.Network.Main)
      };

      SignedPayload signed = JsonSerializer.Deserialize<SignedPayload>(rawBody);

      byte[] payload = System.Convert.FromBase64String(signed.payload);
      var recovered_hex = PubKey.RecoverFromMessage(payload, signed.signature).ToHex();
      var recovered_pubkey = new RNB.PubKey(recovered_hex);
      var recovered_address = recovered_pubkey.GetAddress(RNB.ScriptPubKeyType.Segwit, constata_addr_network).ToString();

      if (recovered_address != constata_addr) {
        throw new Exception("Unexpected web callback not signed by constata");
      }

      JsonNode json = JsonNode.Parse(Encoding.UTF8.GetString(payload));
      this.Kind = json["kind"]!.GetValue<string>();
      this.Resource = json["resource"];
    }
  }
}
