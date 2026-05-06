# NodeKit_POC

NodeKit의 `Tool Source Route -> Tool Image Recipe` 흐름을 검증하기 위한 Sprint 1~2 PoC입니다.

## 현재 범위

- Connected / Disconnected 모드 선택
- Tool 시작점 선택
- fixture provider 기반 tool 검색
- 후보 선택 전 preview 확인

## 실행 방법

```bash
dotnet build
dotnet run
```

## 현재 fixture tool

- bwa
- samtools
- gatk
- fastqc
