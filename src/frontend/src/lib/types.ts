export type AssetType = "text" | "image" | "video" | "music";

export interface AssetTaggingInfo {
  provider?: string | null;
  model?: string | null;
  description?: string | null;
  tags?: string[];
  raw_text?: string | null;
  tagged_at?: string | null;
}

export interface LibraryItem {
  id: string;
  name: string;
  root_path: string;
  created_at: string;
  updated_at: string;
}

export interface LibraryListResponse {
  items: LibraryItem[];
  total: number;
  stage: string;
}

export interface LibraryCreateRequest {
  id: string;
  name: string;
  root_path: string;
}

export interface AssetItem {
  id: string;
  library_id: string;
  library_name: string;
  name: string;
  asset_type: AssetType | string;
  path: string;
  relative_path: string;
  description: string;
  tags: string[];
  status: string;
  tagging?: AssetTaggingInfo | null;
}

export interface AssetListResponse {
  items: AssetItem[];
  total: number;
  stage: string;
}

export interface TaggingRequest {
  asset_id: string;
  asset_type: AssetType;
  source_path: string;
  text?: string | null;
  media_mime_type?: string | null;
  title?: string | null;
}

export interface TaggingResponse {
  asset_id: string;
  asset_type: string;
  source_path: string;
  provider: string;
  model: string;
  description: string;
  tags: string[];
  raw_text: string;
  stage: string;
}
