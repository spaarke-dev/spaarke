// CURATED EXCERPT — see SOURCE.md for full file location and licence.
// This excerpt isolates the container and container-permission HTTP calls
// against Microsoft Graph `/storage/fileStorage/containers` so reviewers can
// see exactly which Graph endpoints SPE uses without wading through 527 LOC
// of unrelated DriveItem helpers.
//
// Original: SharePoint-Embedded-Samples / Custom Apps / boilerplate-aspnet-webservice
//           / Services / MSGraphService.cs (lines 25-200, 485-500)
// Licence: MIT (see SOURCE.md)

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Demo.Exceptions;
using Demo.Models;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Demo.Services
{
    /// <summary>
    /// SPE container + permission CRUD via direct Microsoft Graph HTTP.
    /// Note: Uses the beta endpoint (`beta/storage/fileStorage/containers`).
    /// </summary>
    public class MSGraphService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MSGraphService> _logger;
        private const string GraphContainersEndpoint = "beta/storage/fileStorage/containers";

        public MSGraphService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<MSGraphService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ─── Container CRUD ──────────────────────────────────────────────────

        public async Task<ContainerModel> AddContainer(string accessToken, ContainerModel container)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.PostAsJsonAsync($"{GraphContainersEndpoint}", container);

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't create the container, status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }

            return response.Content.ReadAsAsync<ContainerModel>().Result;
        }

        public async Task ActivateContainer(string accessToken, string containerId)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.PostAsync($"{GraphContainersEndpoint}/{containerId}/activate", null);

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't activate the container, status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
        }

        public async Task<ContainerModel> GetContainer(string accessToken, string containerId)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.GetAsync($"{GraphContainersEndpoint}/{containerId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't get the container, status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }

            return response.Content.ReadAsAsync<ContainerModel>().Result;
        }

        public async Task<ContainerModel> UpdateContainer(string accessToken, string containerId, ContainerModel container)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            string serialized = JsonConvert.SerializeObject(container, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            HttpContent content = new StringContent(serialized, Encoding.UTF8, "application/json");
            var response = await client.PatchAsync($"{GraphContainersEndpoint}/{containerId}", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't update the container, status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }

            return response.Content.ReadAsAsync<ContainerModel>().Result;
        }

        public async Task DeleteContainer(string accessToken, string containerId)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.DeleteAsync($"{GraphContainersEndpoint}/{containerId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't delete the container. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
        }

        public async Task<IEnumerable<ContainerModel>> GetAllContainers(string accessToken)
        {
            // List containers belonging to this app's container type (filtered by containerTypeId).
            string containerTypeId = _configuration.GetValue<string>("TestContainer:containerTypeId");
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.GetAsync($"{GraphContainersEndpoint}?$filter=containerTypeId eq {containerTypeId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't get the list of containers. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
            string content = await response.Content.ReadAsStringAsync();
            JObject deserialized = JsonConvert.DeserializeObject<JObject>(content);
            JArray array = deserialized.Value<JArray>("value");
            return array.ToObject<List<ContainerModel>>();
        }

        // ─── Container Permission CRUD ───────────────────────────────────────

        public async Task<IEnumerable<ContainerPermissionModel>> GetContainerPermissions(string accessToken, string containerId)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.GetAsync($"{GraphContainersEndpoint}/{containerId}/permissions");

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't get the container's permissions. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
            string content = await response.Content.ReadAsStringAsync();
            JObject deserialized = JsonConvert.DeserializeObject<JObject>(content);
            JArray array = deserialized.Value<JArray>("value");
            return array.ToObject<List<ContainerPermissionModel>>();
        }

        public async Task<ContainerPermissionModel> UpdateContainerPermission(string accessToken, string containerId, string permissionId, string role)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var json = $@"{{ ""roles"":[""{role}""]}}";
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PatchAsync($"{GraphContainersEndpoint}/{containerId}/permissions/{permissionId}", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't update the container's permission. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
            return response.Content.ReadAsAsync<ContainerPermissionModel>().Result;
        }

        public async Task<ContainerPermissionModel> AddContainerPermission(string accessToken, string containerId, ContainerPermissionModel permission)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            string serialized = JsonConvert.SerializeObject(permission, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            HttpContent content = new StringContent(serialized, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{GraphContainersEndpoint}/{containerId}/permissions", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't add the permission to the container. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
            return response.Content.ReadAsAsync<ContainerPermissionModel>().Result;
        }

        public async Task DeleteContainerPermission(string accessToken, string containerId, string permissionId)
        {
            HttpClient client = GetHttpClient(accessToken, "application/json");
            var response = await client.DeleteAsync($"{GraphContainersEndpoint}/{containerId}/permissions/{permissionId}");

            if (!response.IsSuccessStatusCode)
            {
                throw new ContainerException($"We couldn't delete the permission. Status code {(int)response.StatusCode}, reason: {response.ReasonPhrase}");
            }
        }

        // ─── HTTP client helper ──────────────────────────────────────────────

        private HttpClient GetHttpClient(string token, string responseMediaType = null)
        {
            HttpClient client = _httpClientFactory.CreateClient();
            client.BaseAddress = new System.Uri("https://graph.microsoft.com");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
            if (responseMediaType != null)
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(responseMediaType));
            return client;
        }
    }
}
