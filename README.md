# NodeKit_POC

NodeKit의 `Tool Runtime Image 만들기` 흐름을 검증하기 위한 독립 authoring 앱입니다.

## 현재 범위

- Connected / Disconnected 모드 선택
- Tool 시작점 선택
- tool 후보 검색과 source route별 preview
- `ToolSourceCandidate -> ToolInstallRecipe -> GeneratedToolImageRecipe` 정규화
- `environment.yml` / multi-stage Dockerfile / lock metadata preview

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

## 정책

- 하나의 이미지 = 하나의 Primary Tool runtime
- Seed runtime image는 특정 command/entrypoint를 고정하지 않음
- 실제 실행 script, command, entrypoint는 이후 wrapper 또는 DAG node 이미지 단계에서 정의
