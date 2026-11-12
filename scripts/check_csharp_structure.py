#!/usr/bin/env python3
"""Conservative C# lexer structural check for unbalanced delimiters and unterminated strings/comments.

NOT a C# grammar parser or Roslyn compiler. Windows CI must run dotnet build.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
FILES = sorted((ROOT / 'src').rglob('*.cs')) + sorted((ROOT / 'tests').rglob('*.cs'))
PAIR = {')': '(', ']': '[', '}': '{'}
errors=[]

def linecol(text, index):
    return text.count('\n',0,index)+1, index - text.rfind('\n',0,index)

def check(path):
    text = path.read_text(encoding='utf-8-sig')
    stack=[]
    i=0
    n=len(text)
    while i<n:
        c=text[i]
        # Line / block comments
        if text.startswith('//',i):
            end=text.find('\n',i+2)
            i=end if end>=0 else n
            continue
        if text.startswith('/*',i):
            end=text.find('*/',i+2)
            if end<0:
                errors.append(f'{path.relative_to(ROOT)}:{linecol(text,i)} unterminated block comment')
                return
            i=end+2
            continue
        if c=='"':
            # Raw strings: 3 or more consecutive quotes delimit the whole string.
            count=0
            while i+count<n and text[i+count]=='"': count+=1
            if count>=3:
                delimiter='"'*count
                end=text.find(delimiter,i+count)
                if end<0:
                    errors.append(f'{path.relative_to(ROOT)}:{linecol(text,i)} unterminated raw string')
                    return
                i=end+count
                continue
            # Verbatim (possibly interpolated) quoted strings.
            verbatim=i>0 and text[i-1]=='@'
            j=i+1
            while j<n:
                if text[j]=='"':
                    if verbatim and j+1<n and text[j+1]=='"':
                        j+=2;continue
                    break
                if not verbatim and text[j]=='\\':
                    j+=2;continue
                j+=1
            if j>=n:
                errors.append(f'{path.relative_to(ROOT)}:{linecol(text,i)} unterminated quoted string')
                return
            i=j+1
            continue
        if c=="'":
            j=i+1
            while j<n and text[j]!="'":
                j+=2 if text[j]=='\\' else 1
            if j>=n:
                errors.append(f'{path.relative_to(ROOT)}:{linecol(text,i)} unterminated char literal')
                return
            i=j+1
            continue
        if c in '([{': stack.append((c,i))
        elif c in ')]}':
            if not stack or stack[-1][0] != PAIR[c]:
                errors.append(f'{path.relative_to(ROOT)}:{linecol(text,i)} mismatched closing {c}')
                return
            stack.pop()
        i+=1
    if stack:
        symbol, position=stack[-1]
        errors.append(f'{path.relative_to(ROOT)}:{linecol(text,position)} unmatched opening {symbol}')

for file in FILES:
    check(file)
if errors:
    print('\n'.join('FAIL '+msg for msg in errors))
    sys.exit(1)
print(f'PASS C# lexical delimiter/string/comment sanity for {len(FILES)} files (NOT compiler validation).')
