import json, urllib.request, urllib.error, base64, hmac, hashlib, time

base_url = 'http://localhost:5000'

headers = {}

def http(method, path, data=None, headers=None):
    url = base_url + path
    req = urllib.request.Request(url, data=data, method=method)
    if headers:
        for k, v in headers.items():
            req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=15) as res:
            body = res.read().decode('utf-8')
            return res.status, dict(res.headers), body
    except urllib.error.HTTPError as e:
        body = e.read().decode('utf-8')
        return e.code, dict(e.headers), body

print('1) GET /api/quotes (unauthenticated)')
status, headers, body = http('GET', '/api/quotes')
print(status)
print('WWW-Authenticate:', headers.get('WWW-Authenticate'))
print(body[:400])
print('---')

print('2) POST /api/quotes (unauthenticated)')
quote_data = json.dumps({'Author':'Test','Text':'Hello from JWT test'}).encode('utf-8')
status, headers, body = http('POST', '/api/quotes', data=quote_data, headers={'Content-Type':'application/json'})
print(status)
print('WWW-Authenticate:', headers.get('WWW-Authenticate'))
print(body)
print('---')

print('3) LOGIN /api/auth/login')
login_data = json.dumps({'Email':'test@example.com','Password':'Password123!'}).encode('utf-8')
status, headers, body = http('POST', '/api/auth/login', data=login_data, headers={'Content-Type':'application/json'})
print(status)
print(body)
if status != 200:
    raise SystemExit('Login failed')
resp = json.loads(body)
token = resp['accessToken']
print('token len', len(token))
print('---')

print('4) POST /api/quotes (authenticated)')
status, headers, body = http('POST', '/api/quotes', data=quote_data, headers={'Content-Type':'application/json','Authorization': f'Bearer {token}'})
print(status)
print('Location:', headers.get('Location'))
print(body[:400])
print('---')

print('5) DELETE /api/quotes/1 (unauthenticated)')
status, headers, body = http('DELETE', '/api/quotes/1')
print(status)
print('WWW-Authenticate:', headers.get('WWW-Authenticate'))
print(body)
print('---')

print('6) DELETE /api/quotes/1 (authenticated)')
status, headers, body = http('DELETE', '/api/quotes/1', headers={'Authorization': f'Bearer {token}'})
print(status)
print(body)
print('---')

print('7) expired token validation')
key = b'01234567890123456789012345678901'
header = {'alg':'HS256','typ':'JWT'}
payload = {'sub':'1','email':'test@example.com','jti':'expired','iss':'QuotesApi','aud':'QuotesApiClients','exp': int(time.time())-60}

def b64u(obj):
    s = json.dumps(obj, separators=(',',':')).encode('utf-8')
    return base64.urlsafe_b64encode(s).rstrip(b'=').decode('ascii')

header_b = b64u(header)
payload_b = b64u(payload)
msg = f'{header_b}.{payload_b}'.encode('ascii')
sig = hmac.new(key, msg, hashlib.sha256).digest()
sig_b = base64.urlsafe_b64encode(sig).rstrip(b'=').decode('ascii')
expired_token = f'{header_b}.{payload_b}.{sig_b}'
print('expired token:', expired_token)
status, headers, body = http('POST', '/api/quotes', data=quote_data, headers={'Content-Type':'application/json','Authorization': f'Bearer {expired_token}'})
print(status)
print('WWW-Authenticate:', headers.get('WWW-Authenticate'))
print(body)
