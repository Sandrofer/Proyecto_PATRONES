// Blocks characters a field does not accept before they reach it (typing,
// pasting or dropping). Each input declares its rule with data-allow="...".
// This only improves the experience: the same rules are validated again by
// the use cases (InputRules) and by the API (RNF-09).
(() => {
  const rules = {
    digits:   { test: /^[0-9]$/,                 msg: 'Solo se permiten números.' },
    decimal:  { test: /^[0-9.,]$/,               msg: 'Solo números y un separador decimal (, o .).' },
    search:   { test: /^[\p{L}\p{N} .\-_/]$/u,   msg: 'Solo letras, números, espacios y - . _ /' },
    username: { test: /^[\p{L}\p{N}._\-@]$/u,    msg: 'Solo letras, números y . _ - @' },
    name:     { test: /^[\p{L} .'\-]$/u,         msg: 'Solo letras, espacios y . \' -' }
  };

  const ruleOf = el => (el && el.dataset ? rules[el.dataset.allow] : undefined);
  const clean = (rule, text) => [...text].filter(c => rule.test.test(c)).join('');

  let tip;
  function warn(el, rule) {
    el.classList.remove('char-rejected');
    void el.offsetWidth;                       // restart the animation
    el.classList.add('char-rejected');
    if (!tip) {
      tip = document.createElement('div');
      tip.className = 'char-tip';
      tip.setAttribute('role', 'status');
      document.body.appendChild(tip);
    }
    const r = el.getBoundingClientRect();
    tip.textContent = rule.msg;
    tip.style.left = `${Math.max(8, r.left + window.scrollX)}px`;
    tip.style.top = `${r.bottom + window.scrollY + 6}px`;
    tip.classList.add('show');
    clearTimeout(tip.timer);
    tip.timer = setTimeout(() => tip.classList.remove('show'), 1800);
  }

  // Typing (also IME and autocorrect): reject the insertion if it has a
  // forbidden character.
  document.addEventListener('beforeinput', e => {
    const rule = ruleOf(e.target);
    if (!rule || !e.inputType.startsWith('insert') || e.data == null) return;
    if (clean(rule, e.data) !== e.data) {
      e.preventDefault();
      warn(e.target, rule);
    }
  }, true);

  // Pasting / dropping: keep only the allowed characters.
  function insertClean(e, text) {
    const el = e.target, rule = ruleOf(el);
    if (!rule || text == null) return;
    const cleaned = clean(rule, text);
    if (cleaned === text) return;
    e.preventDefault();
    const max = el.maxLength > 0 ? el.maxLength : Infinity;
    const room = max - (el.value.length - (el.selectionEnd - el.selectionStart));
    const text2 = cleaned.slice(0, Math.max(0, room));
    el.focus();
    // insertText behaves like typing: undo works and the browser fires
    // "input" now and "change" on blur, so Blazor's bindings see the value.
    if (!text2 || !document.execCommand('insertText', false, text2)) {
      el.setRangeText(text2, el.selectionStart, el.selectionEnd, 'end');
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    }
    warn(el, rule);
  }
  document.addEventListener('paste', e => insertClean(e, e.clipboardData && e.clipboardData.getData('text')), true);
  document.addEventListener('drop', e => insertClean(e, e.dataTransfer && e.dataTransfer.getData('text')), true);
})();
