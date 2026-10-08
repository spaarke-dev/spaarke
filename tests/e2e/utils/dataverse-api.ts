import axios, { AxiosInstance } from 'axios';
import { ClientSecretCredential } from '@azure/identity';

/**
 * Reusable Dataverse Web API client for test data management
 */
export class DataverseAPI {
  private client: AxiosInstance;
  private baseUrl: string;

  constructor(baseUrl: string, accessToken: string) {
    this.baseUrl = baseUrl;
    this.client = axios.create({
      baseURL: baseUrl,
      headers: {
        Authorization: `Bearer ${accessToken}`,
        'OData-MaxVersion': '4.0',
        'OData-Version': '4.0',
        Accept: 'application/json',
        'Content-Type': 'application/json',
      },
    });
  }

  /**
   * Create Azure AD access token for service principal.
   *
   * @param resource - The Dataverse org URL. A Web API URL (`https://org.api.crm.dynamics.com/api/data/v9.2`) is
   *   accepted too: the token is always requested for the ORIGIN (`https://org.api.crm.dynamics.com/.default`), because
   *   the Web API path is not part of the resource and a scope that carries it does not name the Dataverse resource.
   */
  static async authenticate(
    tenantId: string,
    clientId: string,
    clientSecret: string,
    resource: string
  ): Promise<string> {
    const credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    const token = await credential.getToken(`${DataverseAPI.resourceOrigin(resource)}/.default`);
    return token.token;
  }

  /** The origin of a Dataverse org or Web API URL, which is what a token is requested for. */
  static resourceOrigin(resource: string): string {
    try {
      return new URL(resource).origin;
    } catch {
      // Not an absolute URL (for example an unset env var): pass it through so the token request fails visibly.
      return resource.replace(/\/+$/, '');
    }
  }

  /**
   * Create test record
   */
  async createRecord(entityName: string, data: Record<string, any>): Promise<string> {
    const response = await this.client.post(`/${entityName}`, data);
    const recordUrl = response.headers['odata-entityid'] || response.headers['OData-EntityId'];
    const recordId = recordUrl.split('(')[1].split(')')[0];
    return recordId;
  }

  /**
   * Delete test record
   */
  async deleteRecord(entityName: string, recordId: string): Promise<void> {
    await this.client.delete(`/${entityName}(${recordId})`);
  }

  /**
   * Batch delete records (cleanup)
   */
  async deleteRecords(entityName: string, recordIds: string[]): Promise<void> {
    const batch = recordIds.map(id =>
      this.client.delete(`/${entityName}(${id})`).catch(() => {
        // Ignore errors for already deleted records
      })
    );
    await Promise.allSettled(batch);
  }

  /**
   * Query records with FetchXML
   */
  async fetchRecords(entityName: string, fetchXml: string): Promise<any[]> {
    const response = await this.client.get(`/${entityName}`, {
      params: { fetchXml },
    });
    return response.data.value;
  }

  /**
   * Execute Custom API (for testing custom commands)
   */
  async executeCustomAPI(apiName: string, parameters: Record<string, any>): Promise<any> {
    const response = await this.client.post(`/${apiName}`, parameters);
    return response.data;
  }

  /**
   * Get record by ID
   */
  async getRecord(entityName: string, recordId: string): Promise<any> {
    const response = await this.client.get(`/${entityName}(${recordId})`);
    return response.data;
  }

  /**
   * Query records using OData query parameters ($filter, $select, $top, etc.).
   * Returns the typed value array from the OData response.
   *
   * @param entityName - Dataverse entity set name (e.g. 'sprk_projects', 'businessunits')
   * @param params - OData query parameters as string key-value pairs
   */
  async queryRecords<T = Record<string, unknown>>(entityName: string, params: Record<string, string>): Promise<T[]> {
    const response = await this.client.get(`/${entityName}`, { params });
    return (response.data?.value ?? []) as T[];
  }
}
