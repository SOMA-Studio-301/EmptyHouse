# Empty House (빈 집)

> "버스 한 대가 마지막 거점 — 좀비로 위장해 감염 지대를 건넌다."

1~4인 협동 생존 호러 · PC (Steam) · Unity 3D (URP) · Unity Netcode (NGO) + UGS
SW마에스트로 17기 · Studio 301

게임 디자인 **정본(기획서)**: `Docs/빈집_기획서.docx` — 항상 최신본이며 모든 설계 결정의 단일 출처다. `.docx` 라 일반 편집기 또는 `python-docx` 로 열람한다. **읽기 전용**(작성/관리는 기획 담당). 이 파일은 저장소에 추적되지 않으며(`.gitignore`), 별도 Resource 리포에서 `Docs/` 경로로 받아온다.

---

## 저장소 구조 — 2계층 에이전트 운영

이 저장소는 작업 디렉터리(cwd)에 따라 두 계층의 AI 에이전트(Claude Code)로 운영된다.

| 계층 | 작업 위치(cwd) | 지침 | 역할 |
| --- | --- | --- | --- |
| **오케스트레이터** | 저장소 루트 `01_EmptyHouse/` | `.claude/CLAUDE.md`(라우터) → `.claude/orchestrator.md` | 기획서 기반 전체 아키텍처 설계·관리. 코드는 직접 작성하지 않고 코드 에이전트에 위임 |
| **코드 에이전트** | `EmptyHouse/` (Unity 프로젝트) | `EmptyHouse/.claude/CLAUDE.md` | C# 구현, Unity 에디터 작업, 코드 레벨 관리 |

루트 `.claude/CLAUDE.md` 는 라우터다 — 트리 전체에 자동 로드되지만, `EmptyHouse/` 내부 코드 작업 시에는 코드 에이전트 지침이 우선한다.

```
01_EmptyHouse/
├── .claude/                  # 오케스트레이터 설정 (.gitignore — 로컬 전용, 자동 로드)
│   ├── CLAUDE.md             #   라우터 (역할 분기 + 스코프 가드)
│   └── orchestrator.md       #   오케스트레이터 플레이북
├── Docs/빈집_기획서.docx      # 게임 디자인 정본 (읽기 전용)
├── README.md                 # 이 문서 (프로젝트 개요 + Git 컨벤션 단일 출처)
├── .github/                  # 이슈/PR 템플릿, CI 워크플로우(unity-release / unity-tests)
└── EmptyHouse/               # Unity 프로젝트 — 코드 에이전트 영역
    └── .claude/CLAUDE.md      # 코드 레벨 지침
```

---

## Git 컨벤션

> 이 절이 커밋/브랜치/버전 규칙의 **단일 출처(single source of truth)** 다. 각 에이전트 지침의 Git 절은 이 문서의 요약본이며, 변경 시 이 문서를 우선한다.

### 커밋 태그(TAG)

| 태그 | 설명 |
| --- | --- |
| feat | 새로운 기능 추가 |
| fix | 버그 수정 |
| art | 아트 에셋 추가/수정 |
| refactor | 코드 리팩토링 (동작 변화 없음) |
| perf | 성능 개선 |
| docs | 문서 수정 (README 등) |
| chore | 빌드/패키지/설정 등 부수 작업 |
| build | 빌드 산출물 반영 |
| demo | 데모/튜토리얼 관련 변경 |

### 커밋 메시지 형식

```
[vX.Y.Z] <type>(<#issue>): <설명>
```

- `[vX.Y.Z]` : 버전과 무관한 작업(`chore`, `docs` 등)은 생략
- `(<#issue>)` : 이슈 없으면 괄호째 생략
- `<설명>` : 한국어 권장. 영어 작성 시 첫 글자 대문자
- **금지: 커밋 메시지에 `Co-Authored-By` 등 AI/Claude 공동작성자 트레일러를 절대 넣지 않는다**

예시:
```
[v0.1.0] feat(#12): 좀비 위장 진입 상호작용 추가
[v0.1.0] fix(#15): 근접 보이스 거리 감쇠 계산 오류 수정
chore: .gitignore 갱신
```

### 브랜치 형식

```
<TAG>/<주요내용>/<지라 카드 번호>
```

예: `feat/disguise/EH-12`, `feat/outline/EH-22`, `chore/ci`

- **이슈 추적은 지라를 쓴다.** 카드 번호는 `EH-<숫자>` 형식이며 브랜치명 끝에 붙인다
- 카드가 없는 작업(`chore`, `docs` 등)은 카드 번호를 생략한다
- **브랜치를 만들기 전에 카드 번호를 확정한다** — 브랜치명이 지라와 작업을 잇는 고리다
- 커밋 메시지 형식은 바뀌지 않는다. 지라 키는 브랜치명에만 들어간다

### PR 머지 형식

```
[vX.Y.Z] <TAG>/<지라 카드 번호> (<PR NUMBER>)
```

예: `[v0.1.0] FEAT/EH-12 (#20)` — 버전 무관 작업 시 `[vX.Y.Z]` 생략, 카드 없을 시 `<지라 카드 번호>` 생략.

PR 제목을 이 형식에서 `(<PR NUMBER>)` 만 뺀 값(`FEAT/EH-12`)으로 지으면, Squash merge 가 번호를 자동으로 붙여 위 형식이 완성된다.

### 작업 흐름 (브랜치 → 병합)

기능 작업은 `main` 에 직접 커밋하지 않는다. 아래 순서를 끝까지 밟는다.

1. **카드 번호 확정 후 브랜치 생성** — `git checkout -b <TAG>/<주요내용>/EH-<번호>`
2. **커밋** — 위 커밋 메시지 형식. 지라 키는 넣지 않는다
3. **푸시** — `git push -u origin <브랜치>`
4. **PR 생성** — 제목은 `<TAG 대문자>/EH-<번호>`, 본문에 변경 요약과 검증 방법
5. **`main` 으로 복귀** — `git checkout main`. PR 을 올린 뒤 브랜치에 머물지 않는다
6. **병합 가능하면 병합** — 체크가 모두 통과하고 충돌이 없으면 Squash merge 로 병합한 뒤 원격 브랜치를 삭제하고, 로컬에서 `git pull` 로 `main` 을 갱신한다

**병합 판단 기준.** 아래 중 하나라도 걸리면 병합하지 말고 사용자에게 상태를 보고한다.

- CI 체크 실패 또는 진행 중
- 충돌 발생 (`MERGEABLE` 이 아님)
- 리뷰 요청이 걸려 있고 승인이 없음
- 릴리즈 태그가 붙을 버전 작업 — 태그는 `main` 커밋에만 붙으므로 병합 시점을 사용자가 정한다

**에이전트 주의.** 3~6 은 원격에 영향을 주는 되돌리기 어려운 작업이다. 사용자가 명시적으로 지시했을 때만 수행하고, 병합은 위 기준을 먼저 확인한 뒤 진행한다.

> **`gh` CLI 는 이 저장소 개발 환경에 설치되어 있지 않다.** 4·6 단계는 GitHub 웹에서 처리하거나, 자동화가 필요하면 `winget install GitHub.cli` 후 `gh auth login` 을 먼저 마쳐야 한다. 푸시(3)와 복귀(5)는 `git` 만으로 가능하다.

### 버전 규칙

| 형식 | 예 | 용도 |
| --- | --- | --- |
| `vMAJOR.MINOR.PATCH` | `v1.0.0` | 정식 릴리즈 |
| `vMAJOR.MINOR.PATCH-preview.N` | `v1.0.0-preview.1` | 내부 테스트/QA 사전 릴리즈 |

- 태그는 반드시 `main` 브랜치 커밋에만 부착 (release 워크플로우 validate job 이 강제)
- 동일 태그 재사용 금지 — preview 재검증 시 번호를 올려 새 태그 생성
- 태그 푸시는 되돌리기 어렵다 — 항상 사용자 확인 후 푸시

---

## 빌드 / 릴리즈

`v*` 태그를 `main` 에 푸시하면 [`.github/workflows/unity-release.yml`](.github/workflows/unity-release.yml) 이 트리거되어 Windows64 빌드 → ZIP(`EmptyHouse-<version>-Windows.zip`) → GitHub Release 까지 자동화한다. 테스트는 [`unity-tests.yml`](.github/workflows/unity-tests.yml).
