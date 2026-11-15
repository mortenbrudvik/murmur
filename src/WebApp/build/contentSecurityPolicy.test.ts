import { describe, expect, it } from 'vitest'
import type { HtmlTagDescriptor, IndexHtmlTransformContext } from 'vite'
import { CONTENT_SECURITY_POLICY, contentSecurityPolicy } from './contentSecurityPolicy.ts'

function transform(plugin: ReturnType<typeof contentSecurityPolicy>): HtmlTagDescriptor[] {
  const hook = plugin.transformIndexHtml
  if (typeof hook !== 'function') throw new Error('expected a function hook')
  return hook.call({} as never, '', {} as IndexHtmlTransformContext) as HtmlTagDescriptor[]
}

describe('contentSecurityPolicy', () => {
  it('applies to the build only, so the dev server keeps HMR', () => {
    expect(contentSecurityPolicy().apply).toBe('build')
  })

  it('prepends a CSP meta tag to the head', () => {
    const tags = transform(contentSecurityPolicy())

    expect(tags).toEqual([
      {
        tag: 'meta',
        attrs: { 'http-equiv': 'Content-Security-Policy', content: CONTENT_SECURITY_POLICY },
        injectTo: 'head-prepend',
      },
    ])
  })

  it('allows only same-origin script and style, with no inline or eval', () => {
    const directives = new Map(
      CONTENT_SECURITY_POLICY.split(';').map((d) => {
        const [name, ...values] = d.trim().split(/\s+/)
        return [name, values] as const
      }),
    )

    expect(directives.get('default-src')).toEqual(["'self'"])
    expect(directives.get('script-src')).toEqual(["'self'"])
    expect(directives.get('style-src')).toEqual(["'self'"])
    expect(CONTENT_SECURITY_POLICY).not.toMatch(/unsafe-|data:|blob:|frame-ancestors/)
  })
})
