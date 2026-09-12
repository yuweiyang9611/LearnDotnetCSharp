import { progress } from "./progress-store.js";
import { copyText } from "./clipboard.js";

export function initializeProgressUI(container) {
  if (!container) return;
  container.classList.add("learning-backup");
  container.innerHTML = `
    <div class="backup-actions"><button type="button" data-export-backup>导出备份</button>
    <button type="button" data-import-backup>导入备份</button><input type="file" accept=".json,application/json" data-backup-file hidden></div>
    <p data-storage-state role="status" aria-live="polite"></p><button type="button" data-retry-save hidden>重试保存</button>
    <p data-backup-message role="status" aria-live="polite"></p>
    <details data-note-copies hidden><summary>历史与冲突笔记副本</summary><div data-copy-list></div></details>
    <dialog data-import-preview aria-labelledby="backup-preview-title"><h2 id="backup-preview-title">合并备份预览</h2>
    <p data-preview-summary></p><p>保留已有完成记录与本地笔记；冲突内容另存副本。</p>
    <div class="backup-actions"><button type="button" data-confirm-import>确认合并</button><button type="button" data-cancel-import>取消</button></div></dialog>`;
  const query = (selector) => container.querySelector(selector);
  const message = query("[data-backup-message]");
  const dialog = query("dialog");
  let pending;
  let renderedCopies;

  function render() {
    query("[data-storage-state]").textContent = progress.saved ? "学习数据已保存在当前浏览器。" : "尚未保存：当前编辑仅保留在本页，可重试保存或导出备份。";
    query("[data-retry-save]").hidden = progress.saved;
    const copies = progress.snapshot().copies;
    query("[data-note-copies]").hidden = copies.length === 0;
    const serialized = JSON.stringify(copies);
    if (serialized === renderedCopies) return;
    renderedCopies = serialized;
    const list = query("[data-copy-list]");
    list.replaceChildren();
    for (const { taskId, record } of copies) {
      const article = document.createElement("article");
      const heading = document.createElement("h3");
      heading.textContent = `${taskId} · ${record.revision}`;
      const text = document.createElement("pre");
      text.textContent = record.notes || "（无笔记，步骤与验收记录已保留在备份中）";
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = "复制副本笔记";
      button.addEventListener("click", async () => { message.textContent = await copyText(record.notes) ? "副本笔记已复制。" : "复制失败，请手动选择笔记复制。"; });
      article.append(heading, text, button);
      list.append(article);
    }
  }

  query("[data-export-backup]").addEventListener("click", () => {
    const url = URL.createObjectURL(new Blob([JSON.stringify(progress.backup(), null, 2)], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `LearnDotnetCSharp-progress-${new Date().toISOString().slice(0, 10)}.json`;
    document.body.append(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    message.textContent = progress.saved ? "备份已生成。" : "已导出包含当前未保存编辑的备份。";
  });
  query("[data-import-backup]").addEventListener("click", () => query("[data-backup-file]").click());
  query("[data-backup-file]").addEventListener("change", async (event) => {
    const file = event.target.files[0];
    event.target.value = "";
    if (!file) return;
    try {
      pending = await progress.readBackup(file);
      const preview = progress.preview(pending);
      query("[data-preview-summary]").textContent = `新增实验完成项 ${preview.experiments}；新增或更新章节 ${preview.chapters}；新增历史/冲突副本 ${preview.copies}；未匹配记录 ${preview.unmatched}。`;
      dialog.showModal();
    } catch (error) { pending = null; message.textContent = error.message; }
  });
  query("[data-confirm-import]").addEventListener("click", () => {
    if (!pending) return;
    const saved = progress.merge(pending);
    pending = null;
    dialog.close();
    message.textContent = saved ? "备份已合并并保存。" : "合并结果尚未保存，可重试或导出当前内容。";
  });
  query("[data-cancel-import]").addEventListener("click", () => { pending = null; dialog.close(); });
  dialog.addEventListener("cancel", () => { pending = null; });
  query("[data-retry-save]").addEventListener("click", () => {
    message.textContent = progress.retry() ? "学习数据已保存。" : "仍未保存，请导出备份保留当前编辑。";
  });
  window.addEventListener("learning-data-change", render);
  render();
}
