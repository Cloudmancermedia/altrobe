import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import { after, before, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js'
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js'
import { z } from 'zod'

// The bridge under test; CI runs the same checks against the bundled copy through BRIDGE.
const bridge = process.env.BRIDGE ?? fileURLToPath(new URL('./bridge.mjs', import.meta.url))

// A stand-in for Altrobe's /mcp: stateless streamable HTTP, one tool, server instructions.
function fakeAltrobe() {
  return createServer(async (req, res) => {
    const server = new McpServer({ name: 'altrobe', version: 'test' }, { instructions: 'Altrobe is a dressing room.' })
    server.registerTool('search_items', { inputSchema: { query: z.string() } }, async ({ query }) => ({
      content: [{ type: 'text', text: `found ${query}` }],
    }))
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined })
    res.on('close', () => { transport.close(); server.close() })
    await server.connect(transport)
    let body = ''
    for await (const chunk of req) body += chunk
    await transport.handleRequest(req, res, body ? JSON.parse(body) : undefined)
  })
}

const listen = (server) => new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server.address().port)))

async function connect(port) {
  const client = new Client({ name: 'test', version: '1' })
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [bridge],
    env: { ...process.env, ALTROBE_URL: `http://127.0.0.1:${port}/mcp` },
    stderr: 'pipe',
  })
  await client.connect(transport)
  return client
}

let altrobe, port
before(async () => { altrobe = fakeAltrobe(); port = await listen(altrobe) })
after(() => altrobe.close())

test('passes tools, calls and server instructions through unchanged', async (t) => {
  const client = await connect(port)
  t.after(() => client.close())
  assert.equal(client.getInstructions(), 'Altrobe is a dressing room.')
  assert.deepEqual((await client.listTools()).tools.map((x) => x.name), ['search_items'])
  const result = await client.callTool({ name: 'search_items', arguments: { query: 'Thunderfury' } })
  assert.deepEqual(result.content, [{ type: 'text', text: 'found Thunderfury' }])
})

test('says to start Altrobe when nothing listens on the port', async () => {
  const closed = createServer()
  const deadPort = await listen(closed)
  await new Promise((r) => closed.close(r))
  await assert.rejects(connect(deadPort), (e) => {
    assert.match(e.message, new RegExp(`Altrobe is not running on port ${deadPort}`))
    return true
  })
})
