import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const customDomain = process.env.CUSTOM_DOMAIN?.trim();
const site = customDomain
  ? `https://${customDomain}`
  : (process.env.SITE_URL ?? 'https://shxwz.github.io');
const base = customDomain
  ? undefined
  : (process.env.BASE_PATH ?? '/IterVC');
const assetBase = base ? base.replace(/\/$/, '') : '';

// Sidebar labels use Starlight locale translations; matching Spanish content lives under
// src/content/docs/es/. More locales can reuse the same route structure.
const navLabel = (label, es) => ({ label, translations: { es } });

export default defineConfig({
  site,
  ...(base ? { base } : {}),
  integrations: [
    starlight({
      head: [
        {
          tag: 'meta',
          attrs: {
            name: 'google-site-verification',
            content: 'Nz3XLWekRwAt4PgHi7wUk2_gsO8Ypp4TT_n67xfc3V0',
          },
        },
      ],
      title: 'IterVC',
      description: 'Official documentation for IterVC, the lightweight Windows application audio router.',
      defaultLocale: 'root',
      locales: {
        root: {
          label: 'English',
          lang: 'en',
        },
        es: {
          label: 'Español',
          lang: 'es',
        },
      },
      logo: {
        src: './src/assets/logo.svg',
        alt: 'IterVC',
      },
      favicon: `${assetBase}/favicon.svg`,
      social: [
        {
          label: 'GitHub',
          icon: 'github',
          href: 'https://github.com/ShxwZ/IterVC',
        },
      ],
      editLink: {
        baseUrl: 'https://github.com/ShxwZ/IterVC/edit/master/docs-site/src/content/docs/',
      },
      customCss: ['./src/styles/custom.css'],
      components: {
        SocialIcons: './src/components/SocialIcons.astro',
      },
      tableOfContents: { minHeadingLevel: 2, maxHeadingLevel: 3 },
      sidebar: [
        { ...navLabel('Home', 'Inicio'), slug: 'index' },
        {
          ...navLabel('Getting started', 'Primeros pasos'),
          items: [
            { ...navLabel('What is IterVC?', '¿Qué es IterVC?'), slug: 'getting-started/overview' },
            { ...navLabel('Requirements', 'Requisitos'), slug: 'getting-started/requirements' },
            { ...navLabel('Installation', 'Instalación'), slug: 'getting-started/installation' },
            { ...navLabel('Quick start', 'Inicio rápido'), slug: 'getting-started/quick-start' },
          ],
        },
        {
          ...navLabel('User guides', 'Guías de uso'),
          items: [
            { ...navLabel('Audio devices', 'Dispositivos de audio'), slug: 'guides/audio-devices' },
            { ...navLabel('Application audio', 'Audio de aplicaciones'), slug: 'guides/application-audio' },
            { ...navLabel('Mix', 'Mezcla'), slug: 'guides/mix' },
            { ...navLabel('Microphone', 'Micrófono'), slug: 'guides/microphone' },
            { ...navLabel('Noise gate', 'Puerta de ruido'), slug: 'guides/noise-gate' },
            { ...navLabel('OSC Chatbox', 'Chatbox OSC'), slug: 'guides/osc-chatbox' },
            { ...navLabel('Global hotkeys', 'Atajos globales'), slug: 'guides/global-hotkeys' },
            { ...navLabel('System tray', 'Bandeja del sistema'), slug: 'guides/system-tray' },
            { ...navLabel('Windows startup', 'Inicio con Windows'), slug: 'guides/windows-startup' },
            { ...navLabel('Update checks', 'Comprobación de actualizaciones'), slug: 'guides/update-checks' },
          ],
        },
        {
          ...navLabel('Troubleshooting', 'Solución de problemas'),
          items: [
            { ...navLabel('No audio', 'No se escucha audio'), slug: 'troubleshooting/no-audio' },
            { ...navLabel('Audio quality', 'Calidad de audio'), slug: 'troubleshooting/audio-quality' },
            { ...navLabel('Applications missing', 'Aplicaciones no detectadas'), slug: 'troubleshooting/applications-missing' },
            { ...navLabel('OSC problems', 'Problemas con OSC'), slug: 'troubleshooting/osc' },
            { ...navLabel('Logs and diagnostics', 'Registros y diagnósticos'), slug: 'troubleshooting/logs' },
          ],
        },
        {
          ...navLabel('Reference', 'Referencia'),
          items: [
            { ...navLabel('Settings and data', 'Ajustes y datos'), slug: 'reference/settings-and-data' },
            { ...navLabel('Audio pipeline', 'Canalización de audio'), slug: 'reference/audio-pipeline' },
            { ...navLabel('FAQ', 'Preguntas frecuentes'), slug: 'reference/faq' },
          ],
        },
        {
          ...navLabel('Development', 'Desarrollo'),
          items: [
            { ...navLabel('Build from source', 'Compilar desde el código fuente'), slug: 'development/building' },
            { ...navLabel('Architecture', 'Arquitectura'), slug: 'development/architecture' },
            { ...navLabel('Contributing', 'Contribuir'), slug: 'development/contributing' },
          ],
        },
      ],
    }),
  ],
});
