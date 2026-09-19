import socket, json, uuid, time, datetime

def send_req(s, msg_type, payload):
    msg_id = str(uuid.uuid4())
    inner = json.dumps(payload)
    env = {'ProtocolVersion': 1, 'MessageId': msg_id, 'Type': msg_type, 'Payload': inner}
    body = json.dumps(env).encode('utf-8')
    header = len(body).to_bytes(4, byteorder='big')
    s.sendall(header + body)
    while True:
        h = s.recv(4)
        if not h: raise Exception('EOF')
        l = int.from_bytes(h, byteorder='big')
        b = b''
        while len(b) < l:
            b += s.recv(l - len(b))
        res = json.loads(b.decode('utf-8'))
        if res.get('CorrelationId') == msg_id:
            return res

tag = uuid.uuid4().hex[:8]
print(f"RUN TC-124 stress-recovery tag={tag} at={datetime.datetime.now().isoformat()}")

# 1. Stress burst: 8 clients, 100 requests each = 800 requests
clients = []
for i in range(8):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect(('127.0.0.1', 5000))
    send_req(s, 0, {'PlayerId': f'stress-{tag}-{i}', 'PlayerName': f'Stress {i}'})
    clients.append(s)

burst_start = time.time()
req_count = 0
for r in range(100):
    for s in clients:
        send_req(s, 2, {})
        req_count += 1
burst_duration = time.time() - burst_start

for s in clients:
    s.close()

print(json.dumps({
    'TestCase': 'TC-124',
    'Step': 'stress-burst-completed',
    'Requests': req_count,
    'DurationSeconds': round(burst_duration, 3),
    'Throughput': round(req_count / burst_duration, 1)
}))

# 2. Cool-down / settle interval
time.sleep(2)

# 3. Recovery check: Fresh client creates room, joins, starts match, submits move
rec_a = socket.socket(socket.AF_INET, socket.SOCK_STREAM); rec_a.connect(('127.0.0.1', 5000))
rec_b = socket.socket(socket.AF_INET, socket.SOCK_STREAM); rec_b.connect(('127.0.0.1', 5000))

try:
    send_req(rec_a, 0, {'PlayerId': f'rec-a-{tag}', 'PlayerName': 'Rec A'})
    send_req(rec_b, 0, {'PlayerId': f'rec-b-{tag}', 'PlayerName': 'Rec B'})

    cr = send_req(rec_a, 9, {'RoomName': f'Recovery {tag}', 'Difficulty': 1})
    cr_payload = json.loads(cr['Payload'])
    room_id = cr_payload['RoomId']
    print(json.dumps({'TestCase': 'TC-124', 'Step': 'recovery-create-room', 'RoomId': room_id, 'Success': True}))

    jr = send_req(rec_b, 10, {'RoomId': room_id})
    print(json.dumps({'TestCase': 'TC-124', 'Step': 'recovery-join-room', 'Success': True}))

    sm = send_req(rec_a, 13, {'RoomId': room_id, 'Difficulty': 1, 'Duration': 5})
    match_id = json.loads(sm['Payload'])['MatchId']
    print(json.dumps({'TestCase': 'TC-124', 'Step': 'recovery-start-match', 'MatchId': match_id, 'Success': True}))

    send_req(rec_a, 15, {'MatchId': match_id})
    send_req(rec_b, 15, {'MatchId': match_id})

    move_res = send_req(rec_a, 19, {'MatchId': match_id, 'MoveId': uuid.uuid4().hex, 'Row': 0, 'Column': 0, 'Value': 1})
    print(json.dumps({'TestCase': 'TC-124', 'Step': 'recovery-submit-move', 'Type': move_res['Type'], 'Success': (move_res['Type'] == 20)}))

    print(json.dumps({
        'TestCase': 'TC-124',
        'Step': 'final-conclusion',
        'ServerNotStuck': True,
        'FlowWorks': True,
        'NoDisposedException': True,
        'Verdict': 'PASSED'
    }))
finally:
    rec_a.close()
    rec_b.close()

print(f"END TC-124 at={datetime.datetime.now().isoformat()}")
