import socket, json, uuid, datetime

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
print(f"RUN TC-119 challenge-spectator-gate tag={tag} at={datetime.datetime.now().isoformat()}")

s_a = socket.socket(socket.AF_INET, socket.SOCK_STREAM); s_a.connect(('127.0.0.1', 5000))
s_b = socket.socket(socket.AF_INET, socket.SOCK_STREAM); s_b.connect(('127.0.0.1', 5000))
s_s = socket.socket(socket.AF_INET, socket.SOCK_STREAM); s_s.connect(('127.0.0.1', 5000))

try:
    # 1. Handshakes
    send_req(s_a, 0, {'PlayerId': f'chall-a-{tag}', 'PlayerName': f'Challenger A {tag}'})
    send_req(s_b, 0, {'PlayerId': f'chall-b-{tag}', 'PlayerName': f'Challenger B {tag}'})
    send_req(s_s, 0, {'PlayerId': f'spec-s-{tag}', 'PlayerName': f'Spectator S {tag}'})

    # 2. Run 1: A challenges B -> B accepts
    c1 = json.loads(send_req(s_a, 25, {'TargetPlayerId': f'chall-b-{tag}', 'Difficulty': 1, 'Duration': 5})['Payload'])
    c1_id = c1['ChallengeId']
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'challenge-1-sent', 'ChallengeId': c1_id, 'Status': c1['Status']}))

    c1_acc = json.loads(send_req(s_b, 28, {'ChallengeId': c1_id})['Payload'])
    room_id = c1_acc['RoomId']
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'challenge-1-accepted', 'ChallengeId': c1_id, 'Status': c1_acc['Status'], 'RoomId': room_id}))

    # Start match in created challenge room
    m1 = json.loads(send_req(s_a, 13, {'RoomId': room_id, 'Difficulty': 1, 'Duration': 5})['Payload'])
    match_id = m1['MatchId']
    send_req(s_a, 15, {'MatchId': match_id})
    send_req(s_b, 15, {'MatchId': match_id})
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'match-started', 'MatchId': match_id, 'State': 1}))

    # 3. Spectator S joins match
    spec_join = send_req(s_s, 32, {'MatchId': match_id})
    spec_state = json.loads(spec_join['Payload'])
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'spectator-joined', 'MatchId': spec_state['MatchId'], 'BoardALen': len(spec_state['BoardA']), 'BoardBLen': len(spec_state['BoardB']), 'Duration': spec_state['Duration']}))

    # Spectator attempts move (must be rejected - read-only)
    spec_move = send_req(s_s, 19, {'MatchId': match_id, 'MoveId': uuid.uuid4().hex, 'Row': 0, 'Column': 0, 'Value': 1})
    spec_move_res = json.loads(spec_move['Payload'])
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'spectator-move-attempt', 'Accepted': spec_move_res.get('Accepted', False), 'ReadOnlyVerified': not spec_move_res.get('Accepted', False)}))

    # Spectator leaves
    spec_leave = send_req(s_s, 33, {'MatchId': match_id})
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'spectator-left', 'Type': spec_leave['Type']}))

    # 4. Run 2: C challenges D -> D rejects (declines)
    s_c = socket.socket(socket.AF_INET, socket.SOCK_STREAM); s_c.connect(('127.0.0.1', 5000))
    s_d = socket.socket(socket.AF_INET, socket.SOCK_STREAM); s_d.connect(('127.0.0.1', 5000))
    send_req(s_c, 0, {'PlayerId': f'chall-c-{tag}', 'PlayerName': f'Challenger C {tag}'})
    send_req(s_d, 0, {'PlayerId': f'chall-d-{tag}', 'PlayerName': f'Challenger D {tag}'})

    c2_resp = send_req(s_c, 25, {'TargetPlayerId': f'chall-d-{tag}', 'Difficulty': 1, 'Duration': 5})
    c2 = json.loads(c2_resp['Payload'])
    c2_id = c2['ChallengeId']
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'challenge-2-sent', 'ChallengeId': c2_id, 'Status': c2['Status']}))

    c2_dec = json.loads(send_req(s_d, 29, {'ChallengeId': c2_id})['Payload'])
    print(json.dumps({'TestCase': 'TC-119', 'Step': 'challenge-2-declined', 'ChallengeId': c2_id, 'Status': c2_dec['Status'], 'RoomId': c2_dec['RoomId'], 'Rejected': (c2_dec['Status'] == 2)}))
    s_c.close(); s_d.close()

    print(f"END TC-119 at={datetime.datetime.now().isoformat()}")
finally:
    s_a.close()
    s_b.close()
    s_s.close()
