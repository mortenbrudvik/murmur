import type { HtmlTagDescriptor, Plugin } from 'vite'

// The bundled app is one script and one stylesheet from its own origin (murmur.localassets.invalid), with no
// inline script, runtime <style>, images, fonts, media or network calls, and the bridge to .NET goes through
// WebView2 host objects, which CSP does not govern. React style={} props set CSSOM properties, which
// style-src does not restrict. base-uri and form-action do not fall back to default-src, so they are named.
// frame-ancestors is left out: browsers ignore it in a <meta> policy.
export const CONTENT_SECURITY_POLICY = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "object-src 'none'",
  "base-uri 'none'",
  "form-action 'none'",
].join('; ')

// Build only: the Vite dev server (Debug builds) injects inline scripts and styles for HMR.
export function contentSecurityPolicy(policy: string = CONTENT_SECURITY_POLICY): Plugin {
  return {
    name: 'murmur:content-security-policy',
    apply: 'build',
    transformIndexHtml(): HtmlTagDescriptor[] {
      return [
        {
          tag: 'meta',
          attrs: { 'http-equiv': 'Content-Security-Policy', content: policy },
          injectTo: 'head-prepend',
        },
      ]
    },
  }
}
