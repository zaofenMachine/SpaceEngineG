"""Local Unity MCP client. No credentials; only the loopback editor endpoint.

python Scripts/unity_rpc.py tools/list '{}'
python Scripts/unity_rpc.py tools/call '{"name":"refresh_unity","arguments":{...}}'
"""
import json
import os
import sys
import urllib.request

URL = os.environ.get('UNITY_MCP_URL', 'http://127.0.0.1:8055/mcp')

def post(method, params, request_id=1, session=None):
    body = {'jsonrpc': '2.0', 'method': method, 'params': params}
    if request_id is not None:
        body['id'] = request_id
    headers = {'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream'}
    if session:
        headers['Mcp-Session-Id'] = session
    req = urllib.request.Request(URL, json.dumps(body).encode(), headers)
    with urllib.request.urlopen(req, timeout=240) as response:
        raw = response.read().decode()
        sid = response.headers.get('Mcp-Session-Id', session)
    if not raw:
        return {}, sid
    if raw.lstrip().startswith('{'):
        return json.loads(raw), sid
    messages = [json.loads(line[5:].strip()) for line in raw.splitlines() if line.startswith('data:')]
    return messages[-1] if messages else {'raw': raw}, sid

def rpc(method, params):
    _, session = post('initialize', {'protocolVersion':'2024-11-05','capabilities':{},'clientInfo':{'name':'spaceengine-validation','version':'1.0'}})
    post('notifications/initialized', {}, None, session)
    result, _ = post(method, params, 2, session)
    return result

if __name__ == '__main__':
    result = rpc(sys.argv[1] if len(sys.argv)>1 else 'tools/list', json.loads(sys.argv[2]) if len(sys.argv)>2 else {})
    print(json.dumps(result, ensure_ascii=False, indent=2))
