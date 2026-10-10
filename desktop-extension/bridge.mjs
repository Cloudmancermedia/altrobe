// Claude Desktop starts local MCP servers over stdio; Altrobe serves MCP over streamable HTTP.
// This relays JSON-RPC messages between the two unchanged, so the tools, their descriptions and
// the server instructions all come from the running Altrobe.
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'

const url = new URL(process.env.ALTROBE_URL || 'http://127.0.0.1:5161/mcp')
const port = url.port || '80'

// Claude Desktop shows stderr in the extension's log; stdout carries the protocol.
const log = (message) => process.stderr.write(`altrobe bridge: ${message}\n`)

function explain(error) {
  const refused = [error, error?.cause, error?.cause?.cause].some((e) => e?.code === 'ECONNREFUSED')
  return refused
    ? `Altrobe is not running on port ${port}. Start Altrobe, then try again.`
    : `Could not reach Altrobe at ${url}: ${error?.message ?? error}`
}

const stdio = new StdioServerTransport()
// Altrobe's endpoint is stateless, so one HTTP transport serves every request, and it keeps
// working when Altrobe is started after Claude Desktop.
const http = new StreamableHTTPClientTransport(url)

http.onmessage = (message) => stdio.send(message)
http.onerror = (error) => log(explain(error))

stdio.onmessage = async (message) => {
  try {
    await http.send(message)
  } catch (error) {
    const reason = explain(error)
    log(reason)
    // Answer requests so the client isn't left waiting; notifications have no one to answer.
    if (message.method && message.id !== undefined)
      await stdio.send({ jsonrpc: '2.0', id: message.id, error: { code: -32000, message: reason } })
  }
}
stdio.onclose = () => { http.close().finally(() => process.exit(0)) }

await http.start()
await stdio.start()
