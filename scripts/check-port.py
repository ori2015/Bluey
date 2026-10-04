#!/usr/bin/env python3
"""Static contract checks; does not compile Swift or prove platform behavior."""
import json
import pathlib
import plistlib
import re

root = pathlib.Path(__file__).resolve().parents[1]
project = (root / 'GooglyEyes.xcodeproj/project.pbxproj').read_text()
for file in (root / 'iOS/Sources').glob('*.swift'):
    assert file.name in project, f'Missing Xcode source: {file.name}'
iphone = '\n'.join(p.read_text() for p in (root / 'iOS/Sources').glob('*.swift'))
assert 'api.openai.com' not in iphone and 'URLSessionWebSocketTask' not in iphone
assert 'requestToken' not in iphone and 'realtimeToken' not in iphone
shared = (root / 'Shared/GooglyLink.swift').read_text()
fields = re.findall(r'public var (\w+):', shared[shared.index('public struct Packet'):shared.index('public extension NWParameters')])
windows = (root / 'Windows/GooglyWindows.Core/Protocol/Packet.cs').read_text()
for field in fields:
    assert re.search(r'\b' + field[0].upper() + field[1:] + r'\b', windows), f'Missing C# wire field: {field}'
for line in (root / 'Windows/GooglyWindows.Tests/Fixtures/iphone-v2.jsonl').read_text().splitlines():
    assert set(json.loads(line)).issubset(set(fields))
info = plistlib.loads((root / 'iOS/Info.plist').read_bytes())
assert '_googly._tcp' in info['NSBonjourServices']
assert all(str(value) for key, value in info.items() if key.startswith('NS') and key.endswith('UsageDescription'))
assert (root / 'iOS/Sources/FaceView.swift').read_bytes()  # Original art preserved; checked separately against upstream.
print('PASS Xcode source references, Swift/C# wire fields, Bonjour declaration, no iPhone OpenAI client')
