import type {
  AssetListResponse,
  LibraryCreateRequest,
  LibraryItem,
  LibraryListResponse,
  TaggingRequest,
  TaggingResponse,
} from "./types";

function resolveApiBase(): string {
  if (import.meta.env.VITE_API_BASE) {
    return import.meta.env.VITE_API_BASE;
  }

  const { hostname, port, protocol } = window.location;
  const isLocalHost = hostname === "localhost" || hostname === "127.0.0.1";

  if (isLocalHost && port === "5173") {
    return "/api/v1";
  }

  if (isLocalHost) {
    return `${protocol}//127.0.0.1:8000/api/v1`;
  }

  return "/api/v1";
}

const API_BASE = resolveApiBase();

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers || {}),
    },
    ...init,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `请求失败：${response.status}`);
  }

  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes("application/json")) {
    const text = await response.text();
    throw new Error(
      text.includes("<!doctype") || text.includes("<html")
        ? "接口返回了 HTML 页面。请确认后端已启动，且前端代理或 API 地址配置正确。"
        : `接口未返回 JSON，实际 content-type: ${contentType || "unknown"}`,
    );
  }

  return (await response.json()) as T;
}

export async function fetchLibraries(): Promise<LibraryListResponse> {
  return requestJson<LibraryListResponse>("/libraries");
}

export async function createLibrary(payload: LibraryCreateRequest): Promise<LibraryItem> {
  return requestJson<LibraryItem>("/libraries", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function fetchAssets(libraryId?: string): Promise<AssetListResponse> {
  const query = libraryId ? `?library_id=${encodeURIComponent(libraryId)}` : "";
  return requestJson<AssetListResponse>(`/assets${query}`);
}

export async function tagAsset(payload: TaggingRequest): Promise<TaggingResponse> {
  return requestJson<TaggingResponse>("/tagging/describe", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}
