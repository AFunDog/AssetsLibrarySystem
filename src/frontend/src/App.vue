<template>
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <div class="brand__mark">AL</div>
        <div>
          <p class="brand__eyebrow">Assets Library System</p>
          <h1>素材工作台</h1>
        </div>
      </div>

      <nav class="nav">
        <button
          v-for="item in navItems"
          :key="item.key"
          class="nav__item"
          :class="{ 'nav__item--active': activePanel === item.key }"
          type="button"
          @click="activePanel = item.key"
        >
          <span>{{ item.label }}</span>
          <small>{{ item.hint }}</small>
        </button>
      </nav>

      <section class="sidebar__card sidebar__card--warning">
        <p class="sidebar__label">当前状态</p>
        <strong>{{ statusText }}</strong>
        <span>{{ selectedAsset ? `已选中 ${selectedAsset.name}` : "尚未选中文件" }}</span>
      </section>

      <section class="sidebar__card">
        <p class="sidebar__label">说明</p>
        <ul class="link-list">
          <li>素材文件来自系统文件系统</li>
          <li>这里记录的是素材库目录</li>
          <li>打标结果按文件路径缓存</li>
        </ul>
      </section>
    </aside>

    <main class="workspace">
      <section class="metrics">
        <MetricCard label="素材库数量" :value="String(libraryCount)" hint="已登记目录" />
        <MetricCard label="文件数量" :value="String(assetCount)" hint="当前扫描结果" />
        <MetricCard label="已打标" :value="String(taggedCount)" hint="按文件缓存结果" />
      </section>

      <section class="content-grid">
        <article class="panel">
          <div class="panel__header">
            <div>
              <p class="panel__eyebrow">素材库目录</p>
              <h3>添加素材库</h3>
            </div>
          </div>

          <form class="form" @submit.prevent="handleCreateLibrary">
            <label>
              <span>素材库名称</span>
              <input v-model="libraryForm.name" type="text" placeholder="例如：角色立绘库" />
            </label>
            <label>
              <span>目录路径</span>
              <div class="form__folder-row">
                <input v-model="libraryForm.root_path" type="text" placeholder="D:\\Assets\\Characters" />
                <button class="button button--ghost button--icon" type="button" @click="toggleBrowser" title="浏览文件夹">
                  📂
                </button>
              </div>
              <div v-if="showBrowser" class="dir-browser">
                <div class="dir-browser__header">
                  <span class="dir-browser__path">{{ browseCurrent || "此电脑" }}</span>
                  <button v-if="browseCurrent" class="dir-browser__up" type="button" @click="browseTo(browseParent)">⬆ 上级</button>
                </div>
                <div v-if="browseLoading" class="dir-browser__empty">加载中...</div>
                <div v-else-if="browseEntries.length === 0" class="dir-browser__empty">此目录下无子文件夹</div>
                <div v-else class="dir-browser__list">
                  <button
                    v-for="entry in browseEntries"
                    :key="entry.path"
                    class="dir-browser__item"
                    type="button"
                    @click="browseTo(entry.path)"
                  >
                    📁 {{ entry.name }}
                  </button>
                </div>
                <div class="dir-browser__footer">
                  <button class="button button--primary button--sm" type="button" @click="confirmBrowse">选择此目录</button>
                  <button class="button button--ghost button--sm" type="button" @click="showBrowser = false">取消</button>
                </div>
              </div>
            </label>
            <button class="button button--primary" type="submit" :disabled="submittingLibrary">
              {{ submittingLibrary ? "添加中..." : "添加素材库目录" }}
            </button>
            <div v-if="libraryError" class="alert alert--error">
              {{ libraryError }}
            </div>
          </form>
        </article>

        <article class="panel">
          <div class="panel__header">
            <div>
              <p class="panel__eyebrow">素材库列表</p>
              <h3>已登记目录</h3>
            </div>
          </div>

          <div v-if="errorMessage" class="alert alert--error">
            {{ errorMessage }}
          </div>

          <div class="asset-list">
            <button
              class="asset-row"
              :class="{ 'asset-row--active': selectedLibraryId === '' }"
              type="button"
              @click="selectLibrary('')"
            >
              <div class="asset-row__main">
                <strong>全部素材库</strong>
                <span>显示所有已扫描文件</span>
              </div>
            </button>

            <button
              v-for="library in libraries"
              :key="library.id"
              class="asset-row"
              :class="{ 'asset-row--active': selectedLibraryId === library.id }"
              type="button"
              @click="selectLibrary(library.id)"
            >
              <div class="asset-row__main">
                <strong>{{ library.name }}</strong>
                <span>{{ library.root_path }}</span>
              </div>
            </button>
          </div>
        </article>

        <article class="panel panel--wide">
          <div class="panel__header">
            <div>
              <p class="panel__eyebrow">素材文件</p>
              <h3>{{ selectedLibraryName }}</h3>
            </div>
            <div class="filters">
              <button
                v-for="type in assetTypeFilters"
                :key="type.value"
                class="chip"
                :class="{ 'chip--active': typeFilter === type.value }"
                type="button"
                @click="typeFilter = type.value"
              >
                {{ type.label }}
              </button>
            </div>
          </div>

          <div class="asset-list asset-list--large">
            <button
              v-for="asset in filteredAssets"
              :key="asset.id"
              class="asset-row"
              :class="{ 'asset-row--active': selectedAsset?.id === asset.id }"
              type="button"
              @click="selectAsset(asset)"
            >
              <div class="asset-row__main">
                <strong>{{ asset.name }}</strong>
                <span>{{ asset.relative_path }}</span>
              </div>
              <div class="asset-row__meta">
                <span class="pill">{{ asset.asset_type }}</span>
                <span class="pill pill--soft">{{ asset.status }}</span>
              </div>
            </button>
            <div v-if="!filteredAssets.length" class="empty-state">
              当前没有扫描到可识别素材。请先添加正确的素材库目录。
            </div>
          </div>
        </article>

        <article class="panel">
          <div class="panel__header">
            <div>
              <p class="panel__eyebrow">文件打标</p>
              <h3>选中文件后执行</h3>
            </div>
          </div>

          <form class="form" @submit.prevent="handleTagAsset">
            <label>
              <span>当前文件</span>
              <input :value="selectedAsset?.name || '请先从文件列表选择'" disabled />
            </label>
            <label>
              <span>文件路径</span>
              <input :value="selectedAsset?.path || ''" disabled />
            </label>
            <label class="form__full">
              <span>附加文本</span>
              <textarea
                v-model="tagForm.text"
                rows="4"
                placeholder="文本素材可直接补充正文；图片/视频可留空，让后端按路径读取"
              ></textarea>
            </label>
            <div class="form__row">
              <label>
                <span>标题</span>
                <input v-model="tagForm.title" type="text" placeholder="可选" />
              </label>
              <label>
                <span>MIME</span>
                <input v-model="tagForm.media_mime_type" type="text" placeholder="image/png" />
              </label>
            </div>
            <button class="button button--accent" type="submit" :disabled="tagging">
              {{ tagging ? "打标中..." : "打标并缓存结果" }}
            </button>
          </form>
        </article>

        <article class="panel">
          <div class="panel__header">
            <div>
              <p class="panel__eyebrow">文件详情</p>
              <h3>{{ selectedAsset?.name || "尚未选择文件" }}</h3>
            </div>
          </div>

          <div v-if="selectedAsset" class="detail-grid detail-grid--single">
            <div class="detail-block">
              <span>来源素材库</span>
              <strong>{{ selectedAsset.library_name }}</strong>
              <p>{{ selectedAsset.path }}</p>
            </div>
            <div class="detail-block">
              <span>标签</span>
              <div class="tag-row">
                <span v-for="tag in selectedAsset.tags" :key="tag" class="tag">{{ tag }}</span>
                <span v-if="!selectedAsset.tags.length" class="muted">暂无标签</span>
              </div>
            </div>
            <div class="detail-block">
              <span>描述</span>
              <p>{{ selectedAsset.description || "尚未打标" }}</p>
            </div>
            <div class="detail-block">
              <span>打标器</span>
              <small>
                {{ selectedAsset.tagging?.provider || "provider: -" }}
                /
                {{ selectedAsset.tagging?.model || "model: -" }}
              </small>
            </div>
          </div>
          <div v-else class="empty-state">
            请选择一个文件查看详情。
          </div>
        </article>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import MetricCard from "./components/MetricCard.vue";
import { browseDirectory, createLibrary, fetchAssets, fetchLibraries, tagAsset } from "./lib/api";
import type { AssetItem, AssetType, BrowseResponse, DirectoryEntry, LibraryItem } from "./lib/types";

type PanelKey = "libraries" | "files" | "tagging";

const navItems: Array<{ key: PanelKey; label: string; hint: string }> = [
  { key: "libraries", label: "素材库", hint: "目录登记与选择" },
  { key: "files", label: "文件", hint: "扫描出的素材文件" },
  { key: "tagging", label: "打标", hint: "按文件缓存结果" },
];

const assetTypeFilters = [
  { value: "all", label: "全部" },
  { value: "text", label: "文本" },
  { value: "image", label: "图片" },
  { value: "video", label: "视频" },
  { value: "music", label: "音乐" },
] as const;

const activePanel = ref<PanelKey>("libraries");
const loading = ref(false);
const submittingLibrary = ref(false);
const libraryError = ref("");
const tagging = ref(false);

// Directory browser state
const showBrowser = ref(false);
const browseLoading = ref(false);
const browseCurrent = ref("");
const browseParent = ref<string | null>(null);
const browseEntries = ref<DirectoryEntry[]>([]);
const errorMessage = ref("");
const typeFilter = ref<(typeof assetTypeFilters)[number]["value"]>("all");
const libraries = ref<LibraryItem[]>([]);
const assets = ref<AssetItem[]>([]);
const selectedLibraryId = ref("");
const selectedAsset = ref<AssetItem | null>(null);

const libraryForm = reactive({
  name: "",
  root_path: "",
});

const tagForm = reactive({
  text: "",
  title: "",
  media_mime_type: "",
});

const statusText = computed(() => {
  if (loading.value) return "正在扫描素材库";
  if (tagging.value) return "正在打标";
  if (submittingLibrary.value) return "正在添加素材库";
  return "待命";
});

const libraryCount = computed(() => libraries.value.length);
const assetCount = computed(() => assets.value.length);
const taggedCount = computed(() => assets.value.filter((asset) => asset.status === "tagged").length);

const selectedLibraryName = computed(() => {
  if (!selectedLibraryId.value) return "全部素材库文件";
  return libraries.value.find((item) => item.id === selectedLibraryId.value)?.name || "当前素材库";
});

const filteredAssets = computed(() => {
  if (typeFilter.value === "all") return assets.value;
  return assets.value.filter((asset) => asset.asset_type === typeFilter.value);
});

function makeLibraryId(name: string, rootPath: string): string {
  const raw = `${name}-${rootPath}`
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return raw || `library-${Date.now()}`;
}

async function loadLibraries() {
  const response = await fetchLibraries();
  libraries.value = response.items;
}

async function loadAssets(libraryId?: string) {
  const response = await fetchAssets(libraryId);
  assets.value = response.items;
  if (selectedAsset.value) {
    selectedAsset.value = response.items.find((item) => item.id === selectedAsset.value?.id) || null;
  }
}

async function refreshAll() {
  loading.value = true;
  errorMessage.value = "";
  try {
    await loadLibraries();
    await loadAssets(selectedLibraryId.value || undefined);
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

async function toggleBrowser() {
  if (showBrowser.value) {
    showBrowser.value = false;
    return;
  }
  showBrowser.value = true;
  await browseTo("");
}

async function browseTo(path: string | null) {
  browseLoading.value = true;
  try {
    const data: BrowseResponse = await browseDirectory(path || "");
    browseCurrent.value = data.current;
    browseParent.value = data.parent;
    browseEntries.value = data.entries;
  } catch (error) {
    libraryError.value = error instanceof Error ? error.message : "浏览目录失败";
  } finally {
    browseLoading.value = false;
  }
}

function confirmBrowse() {
  if (browseCurrent.value) {
    libraryForm.root_path = browseCurrent.value;
    // Auto-fill library name from folder name if empty
    if (!libraryForm.name) {
      const parts = browseCurrent.value.replace(/[\/\\]$/, "").split(/[\/\\]/);
      libraryForm.name = parts[parts.length - 1] || "";
    }
  }
  showBrowser.value = false;
}

async function handleCreateLibrary() {
  if (!libraryForm.name.trim() || !libraryForm.root_path.trim()) {
    libraryError.value = "请填写素材库名称和目录路径";
    return;
  }

  submittingLibrary.value = true;
  libraryError.value = "";
  errorMessage.value = "";
  try {
    const payload = {
      id: makeLibraryId(libraryForm.name, libraryForm.root_path),
      name: libraryForm.name.trim(),
      root_path: libraryForm.root_path.trim(),
    };
    const created = await createLibrary(payload);
    libraries.value = [...libraries.value.filter((item) => item.id !== created.id), created];
    libraryForm.name = "";
    libraryForm.root_path = "";
    await selectLibrary(created.id);
    activePanel.value = "files";
  } catch (error) {
    const msg = error instanceof Error ? error.message : "添加素材库失败";
    libraryError.value = msg;
    errorMessage.value = msg;
  } finally {
    submittingLibrary.value = false;
  }
}

async function selectLibrary(libraryId: string) {
  selectedLibraryId.value = libraryId;
  selectedAsset.value = null;
  tagForm.text = "";
  tagForm.title = "";
  tagForm.media_mime_type = "";

  loading.value = true;
  errorMessage.value = "";
  try {
    await loadAssets(libraryId || undefined);
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : "扫描素材库失败";
  } finally {
    loading.value = false;
  }
}

function selectAsset(asset: AssetItem) {
  selectedAsset.value = asset;
  tagForm.title = asset.name;
  tagForm.text = asset.asset_type === "text" ? asset.description : "";
  tagForm.media_mime_type = defaultMime(asset.asset_type as AssetType);
  activePanel.value = "tagging";
}

function defaultMime(assetType: AssetType): string {
  if (assetType === "image") return "image/png";
  if (assetType === "video") return "video/mp4";
  if (assetType === "music") return "audio/mpeg";
  return "text/plain";
}

async function handleTagAsset() {
  if (!selectedAsset.value) {
    errorMessage.value = "请先选择一个文件";
    return;
  }

  tagging.value = true;
  errorMessage.value = "";
  try {
    const response = await tagAsset({
      asset_id: selectedAsset.value.id,
      asset_type: selectedAsset.value.asset_type as AssetType,
      source_path: selectedAsset.value.path,
      text: tagForm.text.trim() || null,
      media_mime_type: tagForm.media_mime_type.trim() || null,
      title: tagForm.title.trim() || null,
    });

    assets.value = assets.value.map((item) =>
      item.id === response.asset_id
        ? {
            ...item,
            status: "tagged",
            description: response.description,
            tags: response.tags,
            tagging: {
              provider: response.provider,
              model: response.model,
              description: response.description,
              tags: response.tags,
              raw_text: response.raw_text,
              tagged_at: new Date().toISOString(),
            },
          }
        : item,
    );
    selectedAsset.value = assets.value.find((item) => item.id === response.asset_id) || null;
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : "打标失败";
  } finally {
    tagging.value = false;
  }
}

onMounted(() => {
  void refreshAll();
});
</script>
