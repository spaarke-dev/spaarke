# @spaarke/sdap-client

SDAP (SharePoint Document Access Platform) API client for PCF controls and TypeScript applications.

## Features

- ✅ Small file upload (< 4MB)
- ✅ Chunked upload for large files (≥ 4MB) with progress tracking
- ✅ File download with streaming
- ✅ File deletion
- ✅ Metadata retrieval
- ✅ TypeScript type definitions
- ✅ Production-ready error handling

## Installation

### From local file

```bash
npm install ../../../packages/sdap-client/spaarke-sdap-client-1.0.0.tgz
```

### From Azure Artifacts (future)

```bash
npm install @spaarke/sdap-client
```

## Usage

### Initialize Client

```typescript
import { SdapApiClient } from '@spaarke/sdap-client';

const client = new SdapApiClient({
  baseUrl: 'https://spe-bff-api.azurewebsites.net',
  timeout: 300000 // 5 minutes
});
```

### Upload File

Automatically uses small upload (< 4MB) or chunked upload (≥ 4MB):

```typescript
const file = new File(['content'], 'document.txt');

const driveItem = await client.uploadFile(containerId, file, {
  onProgress: (percent) => {
    console.log(`Upload progress: ${percent}%`);
  }
});

console.log('Uploaded:', driveItem.id, driveItem.name);
```

### Upload Large File (Chunked Upload)

Files ≥ 4MB automatically use chunked upload with 320 KB chunks:

```typescript
const largeFile = new File(['...'], 'large-document.pdf'); // > 4MB

const driveItem = await client.uploadFile(containerId, largeFile, {
  onProgress: (percent) => {
    console.log(`Upload progress: ${percent}%`);
    // Updates during each chunk upload
  }
});
```

### Download File

```typescript
const blob = await client.downloadFile(driveId, itemId);

// Trigger browser download
const url = URL.createObjectURL(blob);
const a = document.createElement('a');
a.href = url;
a.download = 'document.txt';
a.click();
URL.revokeObjectURL(url);
```

### Delete File

```typescript
await client.deleteFile(driveId, itemId);
```

### Get File Metadata

```typescript
const metadata = await client.getFileMetadata(driveId, itemId);
console.log(metadata.name, metadata.size, metadata.mimeType);
```

## Error Handling

```typescript
try {
  await client.uploadFile(containerId, file);
} catch (error) {
  if (error.message.includes('401')) {
    // Authentication error
  } else if (error.message.includes('403')) {
    // Permission error
  } else if (error.message.includes('404')) {
    // File not found
  } else if (error.message.includes('timeout')) {
    // Request timed out
  } else {
    // Other error
  }
}
```

## TypeScript Types

```typescript
import {
  SdapClientConfig,
  DriveItem,
  UploadSession,
  FileMetadata,
  UploadProgressCallback,
  SdapApiError,
  Container
} from '@spaarke/sdap-client';
```

### SdapClientConfig

```typescript
interface SdapClientConfig {
  baseUrl: string;      // SDAP BFF API URL
  timeout?: number;     // Request timeout (default: 300000ms)
}
```

### DriveItem

```typescript
interface DriveItem {
  id: string;
  name: string;
  size: number | null;
  driveId: string;
  createdDateTime: string;
  lastModifiedDateTime: string;
  isFolder: boolean;
  mimeType?: string;
}
```

## Chunked Upload Details

The client automatically selects the upload strategy:

- **Small files (< 4MB)**: Single PUT request
- **Large files (≥ 4MB)**: Chunked upload with 320 KB chunks

**Chunked upload flow:**

1. Create upload session: `POST /api/obo/drives/{driveId}/upload-session`
2. Upload chunks: Multiple `PUT` requests with `Content-Range` headers
3. Server returns 202 (Accepted) for each chunk, 200/201 when complete
4. Progress callback invoked after each chunk

**Benefits:**

- Handles network interruptions better
- Shows progress to users
- Works with large files (tested up to 20MB+)

## Authentication

Authentication is handled automatically by the browser session when running in PCF controls within Dataverse. The SDAP BFF API uses OBO (On-Behalf-Of) authentication with the current user's context.

## Performance

Tested performance benchmarks:

- Small file (< 4MB): < 10 seconds
- Chunked upload (4-20MB): < 2 minutes
- Download (10MB): < 15 seconds
- Package size: 37 KB (gzipped)

## Development

### Build

```bash
npm run build
```

### Test

```bash
npm test
```

### Lint

```bash
npm run lint
```

### Package

```bash
npm pack
```

## License

UNLICENSED - Internal use only

## Support

Contact Sprint 6 team for questions or integration support.

## Version History

### 1.0.0 (2025-10-04)

- Initial release
- Small file upload support
- Chunked upload for large files (≥ 4MB)
- Download and delete operations
- File metadata retrieval
- TypeScript type definitions
- Production-ready error handling
