<template>
  <div class="app-shell">
    <header class="hero">
      <p class="hero__eyebrow">Assets Library System</p>
      <h1>素材管理系统骨架</h1>
      <p class="hero__lead">
        当前阶段只搭结构，不落真实功能。后续会在这个骨架上补齐素材管理、打标、RAG 与自然语言搜索。
      </p>
    </header>

    <main class="grid">
      <StatusCard title="素材范围" :accent="false">
        <ul class="tag-list">
          <li>文本</li>
          <li>图片</li>
          <li>视频</li>
          <li>音乐</li>
        </ul>
      </StatusCard>

      <StatusCard title="后端模块" :accent="false">
        <ul class="feature-list">
          <li>素材目录扫描与元数据管理</li>
          <li>自动打标与摘要生成</li>
          <li>向量索引、召回与精排</li>
          <li>RAG 问答与自然语言搜索</li>
        </ul>
      </StatusCard>

      <StatusCard title="前端工作台" :accent="false">
        <ul class="feature-list">
          <li>素材列表与详情</li>
          <li>标签与描述编辑</li>
          <li>自然语言搜索入口</li>
          <li>后台任务与索引状态</li>
        </ul>
      </StatusCard>

      <StatusCard title="当前状态" :accent="true">
        <p>已建立前后端入口、分层目录和架构文档。</p>
        <p>下一步建议先实现素材元数据与基础列表接口。</p>
      </StatusCard>
    </main>
  </div>
</template>

<script setup lang="ts">
import StatusCard from "./components/StatusCard.vue";
</script>
