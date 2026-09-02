# Changelog

## [1.0.0-rc.1](https://github.com/Siliciummangel/MTSM.Cirrus/compare/v0.2.0...v1.0.0-rc.1) (2026-09-02)


### Features

* **storage:** add asynchronous staging processing lifecycle ([1cae0f7](https://github.com/Siliciummangel/MTSM.Cirrus/commit/1cae0f72442f69395a3904ccc58e10c6acd830b0))
* **storage:** implement content-addressed packing and maintenance ([825993b](https://github.com/Siliciummangel/MTSM.Cirrus/commit/825993b45ba563de944631fe2257889048385816))
* **storage:** stage new archive objects for asynchronous processing ([5839059](https://github.com/Siliciummangel/MTSM.Cirrus/commit/5839059ee7b786b95c659d3941b8ffc23b56da5c))


### Fixes

* **api:** register TimeProvider for API key authentication ([46e7878](https://github.com/Siliciummangel/MTSM.Cirrus/commit/46e7878ac920abb3f17eeda1bba7b0e8c96460d3))
* **api:** register web encoders for API key authentication ([87c2e70](https://github.com/Siliciummangel/MTSM.Cirrus/commit/87c2e70344f390170d652c46fd700da1a0e4b050))
* **api:** remove unused DataProtection keyring ([91c16e3](https://github.com/Siliciummangel/MTSM.Cirrus/commit/91c16e33b7d37f54cd42ad682df0c6431a138b2d))
* **api:** stream multipart uploads without disk buffering ([ed49220](https://github.com/Siliciummangel/MTSM.Cirrus/commit/ed49220948848881a69df3890e75fee5513be3e3))
* **api:** stream multipart uploads without disk buffering ([88843c1](https://github.com/Siliciummangel/MTSM.Cirrus/commit/88843c17e44d62700c7bc327bdb904fe313b25cb)), closes [#5](https://github.com/Siliciummangel/MTSM.Cirrus/issues/5)
* **ci:** disable storage processing in worker smoke test ([e258257](https://github.com/Siliciummangel/MTSM.Cirrus/commit/e258257cc04c4f2b53c16cd2959b7b7b022d9dde))
* **storage:** persist staged processing status explicitly ([6f3a510](https://github.com/Siliciummangel/MTSM.Cirrus/commit/6f3a5103e1972ac38c0b647dab86616e118d3907))
* **worker:** execute EF Core transactions within retry strategy ([0667754](https://github.com/Siliciummangel/MTSM.Cirrus/commit/066775490db0b4064050070c8a245925cfc9b34c))
* **worker:** stream pack uploads without disk buffering ([#8](https://github.com/Siliciummangel/MTSM.Cirrus/issues/8)) ([25ef230](https://github.com/Siliciummangel/MTSM.Cirrus/commit/25ef2303b216d171d65667dd802a85ee5e9cc725)), closes [#7](https://github.com/Siliciummangel/MTSM.Cirrus/issues/7)


### Internal

* **release:** prepare 1.0.0-rc.1 ([d29c771](https://github.com/Siliciummangel/MTSM.Cirrus/commit/d29c771cafbcfce84f2fc3639cfd62cf5c8ed273))

## [0.2.0](https://github.com/Siliciummangel/MTSM.Cirrus/compare/v0.1.0...v0.2.0) (2026-08-22)


### Features

* **deployment:** add Kubernetes Helm chart ([db73a8a](https://github.com/Siliciummangel/MTSM.Cirrus/commit/db73a8a35cad662d5edb2296c101a67d49a8d7dd))


### Fixes

* **worker:** make integrity check claims atomic ([c96df8e](https://github.com/Siliciummangel/MTSM.Cirrus/commit/c96df8ec63b03048d1ee5aa057071e484b2405f0))

## 0.1.0 (2026-08-22)


### Features

* **deployment:** automate container publishing and releases ([ca64eed](https://github.com/Siliciummangel/MTSM.Cirrus/commit/ca64eed1c1ac2e28fd8f9dad546e6291258d6324))

## Changelog

Notable Cirrus changes are recorded here automatically from Conventional
Commits when a release pull request is prepared.
