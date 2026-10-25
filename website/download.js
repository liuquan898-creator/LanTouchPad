document.querySelectorAll('[data-copy]').forEach(button => {
  button.addEventListener('click', async () => {
    const status = document.getElementById('status');
    try {
      await navigator.clipboard.writeText(button.dataset.copy);
      status.textContent = '下载链接已复制';
      const original = button.textContent;
      button.textContent = '已复制';
      setTimeout(() => { button.textContent = original; }, 2000);
    } catch {
      const field = document.getElementById('fallback');
      field.hidden = false;
      field.value = button.dataset.copy;
      field.focus(); field.select();
      status.textContent = '剪贴板不可用';
    }
  });
});
