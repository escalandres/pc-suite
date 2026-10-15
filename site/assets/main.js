/* ==========================================================================
   PCSuite landing — pinta la página desde pcsuite/latest.json.
   La página NO se toca entre releases: versión, enlaces, tamaños, fechas y
   SHA-256 salen todos del manifiesto que genera publicar.bat.
   ========================================================================== */
(() => {
  'use strict';

  // Absoluto: la landing vive en /pcsuite/ (p.ej. dentro del repo Astro) y
  // en la raíz durante pruebas locales — en ambos casos este path resuelve.
  const MANIFEST_URL = '/pcsuite/latest.json';

  const $ = (sel) => document.querySelector(sel);
  const $$ = (sel) => Array.from(document.querySelectorAll(sel));

  const formatMb = (bytes) => `${Math.round(bytes / (1024 * 1024))} MB`;

  const formatDate = (iso) => {
    try {
      return new Date(iso).toLocaleDateString('en-GB', {
        day: 'numeric', month: 'long', year: 'numeric',
      });
    } catch {
      return '';
    }
  };

  /** Pinta una tarjeta de artefacto con su hash y comando de verificación. */
  const paintArtifact = (card, artifact) => {
    const slots = {};
    $$('[data-slot]').forEach((el) => { slots[el.dataset.slot] = el; });
    // Scoped al card:
    const slot = (name) => card.querySelector(`[data-slot="${name}"]`);

    const label = slot('label');
    // Labels localizados por id (el manifiesto trae los suyos en español:
    // son datos del updater, la UI traduce). Mirrors src/i18n/pcsuite.ts EN.
    const LABELS = {
      'setup-win64': 'Installer (recommended)',
      'portable-win64': 'Portable (.zip)',
    };
    if (label) label.textContent = LABELS[artifact.id] || artifact.label || artifact.id;

    const size = slot('size');
    if (size) size.textContent = formatMb(artifact.size);

    const url = slot('url');
    if (url) {
      url.href = artifact.url;
      url.removeAttribute('href-disabled');
    }

    const sha = slot('sha');
    if (sha) sha.textContent = artifact.sha256;

    const btnCopy = card.querySelector('[data-copy-slot="sha"]');
    if (btnCopy) {
      btnCopy.dataset.copyValue = artifact.sha256;
    }

    const verify = slot('verify');
    if (verify && artifact.url) {
      const fileName = artifact.url.split('/').pop();
      verify.textContent = `Get-FileHash .\\${fileName} -Algorithm SHA256`;
    }
  };

  const paintManifest = (manifest) => {
    // Versión global
    $$('[data-slot="version"]').forEach((el) => { el.textContent = manifest.version; });
    const vi = $('[data-slot="version-inline"]');
    if (vi) vi.textContent = manifest.version;

    const releasedSlots = $$('[data-slot="released"]');
    const rel = formatDate(manifest.releasedAt);
    releasedSlots.forEach((el) => { el.textContent = rel; });

    // CTA del hero = instalador; CTA secundario = portable
    const setup = manifest.artifacts.find((a) => a.id === 'setup-win64');
    const portable = manifest.artifacts.find((a) => a.id === 'portable-win64');

    if (setup) {
      const main = $('#cta-main');
      if (main) {
        main.href = setup.url;
        main.textContent = `Download ${formatMb(setup.size)}`;
      }
      const mb = $('[data-slot="setup-mb"]');
      if (mb) mb.textContent = String(Math.round(setup.size / (1024 * 1024)));
    }
    if (portable) {
      const alt = $('#cta-alt');
      if (alt) alt.href = portable.url;
    }

    // Tarjetas de descarga
    $$('.artifact-card').forEach((card) => {
      const id = card.dataset.artifact;
      const artifact = manifest.artifacts.find((a) => a.id === id);
      if (artifact) paintArtifact(card, artifact);
    });

    // Notas de versión
    const notes = $('[data-slot="notes"]');
    if (notes && manifest.releaseNotesUrl) {
      notes.href = manifest.releaseNotesUrl;
    }
  };

  const showError = () => {
    const box = $('#download-error');
    if (box) box.hidden = false;
    const cards = $('#download-cards');
    if (cards) cards.hidden = true;
    const main = $('#cta-main');
    if (main) main.textContent = 'Download unavailable';
  };

  // ---------- Copiar al portapapeles (con fallback para contexts no seguros) ----------
  const copyText = async (text) => {
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return true;
      }
    } catch { /* cae al fallback */ }

    try {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand('copy');
      document.body.removeChild(ta);
      return ok;
    } catch {
      return false;
    }
  };

  document.addEventListener('click', async (ev) => {
    const btn = ev.target.closest('.btn-copy');
    if (!btn) return;
    const value = btn.dataset.copyValue;
    if (!value) return;
    const ok = await copyText(value);
    const original = btn.textContent;
    btn.textContent = ok ? '✓ Copied' : 'Copy failed';
    if (ok) btn.classList.add('copied');
    setTimeout(() => {
      btn.textContent = original;
      btn.classList.remove('copied');
    }, 1800);
  });

  // ---------- Carga ----------
  const year = $('#year');
  if (year) year.textContent = String(new Date().getFullYear());

  fetch(MANIFEST_URL, { cache: 'no-cache' })
    .then((r) => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return r.json();
    })
    .then(paintManifest)
    .catch(showError);
})();
