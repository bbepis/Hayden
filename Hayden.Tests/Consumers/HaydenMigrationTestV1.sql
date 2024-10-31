--
-- File generated with SQLiteStudio v3.4.4 on Mon Oct 28 18:01:53 2024
--
-- Text encoding used: UTF-8
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: __EFMigrationsHistory
CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
    MigrationId    TEXT NOT NULL
                        CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
    ProductVersion TEXT NOT NULL
);

INSERT INTO __EFMigrationsHistory (
                                      MigrationId,
                                      ProductVersion
                                  )
                                  VALUES (
                                      'v1_InitialCreate',
                                      '8.0.10'
                                  );


-- Table: bans_user
CREATE TABLE IF NOT EXISTS bans_user (
    ID              INTEGER NOT NULL
                            CONSTRAINT PK_bans_user PRIMARY KEY AUTOINCREMENT,
    IPAddress       BLOB    NOT NULL,
    Reason          TEXT    NOT NULL,
    PublicReason    TEXT    NOT NULL,
    TimeBannedUTC   TEXT    NOT NULL,
    TimeUnbannedUTC TEXT
);


-- Table: boards
CREATE TABLE IF NOT EXISTS boards (
    Id                INTEGER NOT NULL
                              CONSTRAINT PK_boards PRIMARY KEY AUTOINCREMENT,
    ShortName         TEXT    NOT NULL,
    LongName          TEXT    NOT NULL,
    Category          TEXT    NOT NULL,
    IsNSFW            INTEGER NOT NULL,
    MultiImageLimit   INTEGER NOT NULL,
    IsReadOnly        INTEGER NOT NULL,
    ShowsDeletedPosts INTEGER NOT NULL
);

INSERT INTO boards (
                       Id,
                       ShortName,
                       LongName,
                       Category,
                       IsNSFW,
                       MultiImageLimit,
                       IsReadOnly,
                       ShowsDeletedPosts
                   )
                   VALUES (
                       1,
                       'board1',
                       'Board 1',
                       'Test',
                       0,
                       0,
                       0,
                       0
                   );

INSERT INTO boards (
                       Id,
                       ShortName,
                       LongName,
                       Category,
                       IsNSFW,
                       MultiImageLimit,
                       IsReadOnly,
                       ShowsDeletedPosts
                   )
                   VALUES (
                       2,
                       'board2',
                       'Board 2',
                       'Test',
                       0,
                       0,
                       0,
                       0
                   );


-- Table: file_mappings
CREATE TABLE IF NOT EXISTS file_mappings (
    BoardId            INTEGER NOT NULL,
    PostId             INTEGER NOT NULL,
    [Index]            INTEGER NOT NULL,
    FileId             INTEGER,
    Filename           TEXT    NOT NULL,
    IsSpoiler          INTEGER NOT NULL,
    IsDeleted          INTEGER NOT NULL,
    AdditionalMetadata TEXT,
    CONSTRAINT PK_file_mappings PRIMARY KEY (
        BoardId,
        PostId,
        [Index]
    ),
    CONSTRAINT FK_file_mappings_boards_BoardId FOREIGN KEY (
        BoardId
    )
    REFERENCES boards (Id) ON DELETE CASCADE,
    CONSTRAINT FK_file_mappings_files_FileId FOREIGN KEY (
        FileId
    )
    REFERENCES files (Id) 
);

INSERT INTO file_mappings (
                              BoardId,
                              PostId,
                              [Index],
                              FileId,
                              Filename,
                              IsSpoiler,
                              IsDeleted,
                              AdditionalMetadata
                          )
                          VALUES (
                              1,
                              1,
                              1,
                              1,
                              'file',
                              0,
                              0,
                              NULL
                          );

INSERT INTO file_mappings (
                              BoardId,
                              PostId,
                              [Index],
                              FileId,
                              Filename,
                              IsSpoiler,
                              IsDeleted,
                              AdditionalMetadata
                          )
                          VALUES (
                              1,
                              2,
                              1,
                              2,
                              'file',
                              0,
                              0,
                              NULL
                          );

INSERT INTO file_mappings (
                              BoardId,
                              PostId,
                              [Index],
                              FileId,
                              Filename,
                              IsSpoiler,
                              IsDeleted,
                              AdditionalMetadata
                          )
                          VALUES (
                              2,
                              1,
                              1,
                              3,
                              'file',
                              0,
                              0,
                              NULL
                          );

INSERT INTO file_mappings (
                              BoardId,
                              PostId,
                              [Index],
                              FileId,
                              Filename,
                              IsSpoiler,
                              IsDeleted,
                              AdditionalMetadata
                          )
                          VALUES (
                              1,
                              3,
                              1,
                              4,
                              'file',
                              0,
                              0,
                              NULL
                          );

INSERT INTO file_mappings (
                              BoardId,
                              PostId,
                              [Index],
                              FileId,
                              Filename,
                              IsSpoiler,
                              IsDeleted,
                              AdditionalMetadata
                          )
                          VALUES (
                              2,
                              2,
                              1,
                              5,
                              'file',
                              0,
                              0,
                              NULL
                          );


-- Table: files
CREATE TABLE IF NOT EXISTS files (
    Id                 INTEGER NOT NULL
                               CONSTRAINT PK_files PRIMARY KEY AUTOINCREMENT,
    BoardId            INTEGER NOT NULL,
    Md5Hash            BLOB    NOT NULL,
    Sha1Hash           BLOB    NOT NULL,
    Sha256Hash         BLOB    NOT NULL,
    PerceptualHash     BLOB,
    StreamHash         BLOB,
    Extension          TEXT    NOT NULL,
    ThumbnailExtension TEXT,
    FileExists         INTEGER NOT NULL,
    FileBanned         INTEGER NOT NULL,
    ImageWidth         INTEGER,
    ImageHeight        INTEGER,
    Size               INTEGER NOT NULL,
    AdditionalMetadata TEXT,
    CONSTRAINT FK_files_boards_BoardId FOREIGN KEY (
        BoardId
    )
    REFERENCES boards (Id) ON DELETE CASCADE
);

INSERT INTO files (
                      Id,
                      BoardId,
                      Md5Hash,
                      Sha1Hash,
                      Sha256Hash,
                      PerceptualHash,
                      StreamHash,
                      Extension,
                      ThumbnailExtension,
                      FileExists,
                      FileBanned,
                      ImageWidth,
                      ImageHeight,
                      Size,
                      AdditionalMetadata
                  )
                  VALUES (
                      1,
                      1,
                      X'9aa419c5bc88b87232a0eb099bff9db0',
                      X'283fde05f4770d8e97e86f5054b79ddd04778285',
                      X'24106394154a787a6b3cb9dfb7cdfa311fdf073418d4752254a05c55d6cf8f8d',
                      NULL,
                      NULL,
                      'jpg',
                      'jpg',
                      1,
                      0,
                      NULL,
                      NULL,
                      1,
                      NULL
                  );

INSERT INTO files (
                      Id,
                      BoardId,
                      Md5Hash,
                      Sha1Hash,
                      Sha256Hash,
                      PerceptualHash,
                      StreamHash,
                      Extension,
                      ThumbnailExtension,
                      FileExists,
                      FileBanned,
                      ImageWidth,
                      ImageHeight,
                      Size,
                      AdditionalMetadata
                  )
                  VALUES (
                      2,
                      1,
                      X'5fa8403596cac275c00fd1637dc1311a',
                      X'5f747f6541c27162ae1233d06139b3a9442ec80d',
                      X'424cc8e979f4d61c6a38aebfdda5c5e498a0ab0773355b804bad33bab097a462',
                      NULL,
                      NULL,
                      'png',
                      'webp',
                      1,
                      0,
                      NULL,
                      NULL,
                      1,
                      NULL
                  );

INSERT INTO files (
                      Id,
                      BoardId,
                      Md5Hash,
                      Sha1Hash,
                      Sha256Hash,
                      PerceptualHash,
                      StreamHash,
                      Extension,
                      ThumbnailExtension,
                      FileExists,
                      FileBanned,
                      ImageWidth,
                      ImageHeight,
                      Size,
                      AdditionalMetadata
                  )
                  VALUES (
                      3,
                      2,
                      X'9aa419c5bc88b87232a0eb099bff9db0',
                      X'283fde05f4770d8e97e86f5054b79ddd04778285',
                      X'24106394154a787a6b3cb9dfb7cdfa311fdf073418d4752254a05c55d6cf8f8d',
                      NULL,
                      NULL,
                      'jpg',
                      'jpg',
                      1,
                      0,
                      NULL,
                      NULL,
                      1,
                      NULL
                  );

INSERT INTO files (
                      Id,
                      BoardId,
                      Md5Hash,
                      Sha1Hash,
                      Sha256Hash,
                      PerceptualHash,
                      StreamHash,
                      Extension,
                      ThumbnailExtension,
                      FileExists,
                      FileBanned,
                      ImageWidth,
                      ImageHeight,
                      Size,
                      AdditionalMetadata
                  )
                  VALUES (
                      4,
                      1,
                      '31111111111111111111',
                      31111111111111111,
                      '31111111111111111111111111111111',
                      NULL,
                      NULL,
                      'jpg',
                      'jpg',
                      1,
                      0,
                      NULL,
                      NULL,
                      1,
                      NULL
                  );

INSERT INTO files (
                      Id,
                      BoardId,
                      Md5Hash,
                      Sha1Hash,
                      Sha256Hash,
                      PerceptualHash,
                      StreamHash,
                      Extension,
                      ThumbnailExtension,
                      FileExists,
                      FileBanned,
                      ImageWidth,
                      ImageHeight,
                      Size,
                      AdditionalMetadata
                  )
                  VALUES (
                      5,
                      2,
                      '41111111111111111111',
                      41111111111111111,
                      '41111111111111111111111111111111',
                      NULL,
                      NULL,
                      'jpg',
                      'jpg',
                      0,
                      0,
                      NULL,
                      NULL,
                      1,
                      NULL
                  );


-- Table: moderators
CREATE TABLE IF NOT EXISTS moderators (
    Id           INTEGER NOT NULL
                         CONSTRAINT PK_moderators PRIMARY KEY AUTOINCREMENT,
    Username     TEXT    NOT NULL,
    PasswordHash BLOB    NOT NULL,
    PasswordSalt BLOB    NOT NULL,
    Role         INTEGER NOT NULL
);


-- Table: posts
CREATE TABLE IF NOT EXISTS posts (
    BoardId            INTEGER           NOT NULL,
    PostId             [BIGINT UNSIGNED] NOT NULL,
    ThreadId           [BIGINT UNSIGNED] NOT NULL,
    ContentHtml        TEXT,
    ContentRaw         TEXT,
    ContentType        INTEGER           NOT NULL,
    Author             TEXT,
    Tripcode           TEXT,
    Email              TEXT,
    DateTime           TEXT              NOT NULL,
    IsDeleted          INTEGER           NOT NULL,
    PosterIP           BLOB,
    AdditionalMetadata TEXT,
    CONSTRAINT PK_posts PRIMARY KEY (
        BoardId,
        PostId
    ),
    CONSTRAINT FK_posts_boards_BoardId FOREIGN KEY (
        BoardId
    )
    REFERENCES boards (Id) ON DELETE CASCADE
);

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      1,
                      1,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      1,
                      2,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      2,
                      1,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      2,
                      2,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      2,
                      3,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );

INSERT INTO posts (
                      BoardId,
                      PostId,
                      ThreadId,
                      ContentHtml,
                      ContentRaw,
                      ContentType,
                      Author,
                      Tripcode,
                      Email,
                      DateTime,
                      IsDeleted,
                      PosterIP,
                      AdditionalMetadata
                  )
                  VALUES (
                      2,
                      4,
                      1,
                      NULL,
                      NULL,
                      'Yotsuba',
                      NULL,
                      NULL,
                      NULL,
                      '0',
                      0,
                      NULL,
                      NULL
                  );


-- Table: threads
CREATE TABLE IF NOT EXISTS threads (
    BoardId            INTEGER NOT NULL,
    ThreadId           INTEGER NOT NULL,
    Title              TEXT,
    LastModified       TEXT    NOT NULL,
    IsArchived         INTEGER NOT NULL,
    IsDeleted          INTEGER NOT NULL,
    AdditionalMetadata TEXT,
    CONSTRAINT PK_threads PRIMARY KEY (
        BoardId,
        ThreadId
    ),
    CONSTRAINT FK_threads_boards_BoardId FOREIGN KEY (
        BoardId
    )
    REFERENCES boards (Id) ON DELETE CASCADE
);

INSERT INTO threads (
                        BoardId,
                        ThreadId,
                        Title,
                        LastModified,
                        IsArchived,
                        IsDeleted,
                        AdditionalMetadata
                    )
                    VALUES (
                        1,
                        1,
                        NULL,
                        '0',
                        0,
                        0,
                        NULL
                    );

INSERT INTO threads (
                        BoardId,
                        ThreadId,
                        Title,
                        LastModified,
                        IsArchived,
                        IsDeleted,
                        AdditionalMetadata
                    )
                    VALUES (
                        2,
                        1,
                        NULL,
                        '0',
                        0,
                        0,
                        NULL
                    );


-- Index: IX_file_mappings_FileId
CREATE INDEX IF NOT EXISTS IX_file_mappings_FileId ON file_mappings (
    "FileId"
);


-- Index: IX_files_BoardId
CREATE INDEX IF NOT EXISTS IX_files_BoardId ON files (
    "BoardId"
);


-- Index: IX_files_Md5Hash
CREATE INDEX IF NOT EXISTS IX_files_Md5Hash ON files (
    "Md5Hash"
);


-- Index: IX_files_PerceptualHash
CREATE INDEX IF NOT EXISTS IX_files_PerceptualHash ON files (
    "PerceptualHash"
);


-- Index: IX_files_Sha1Hash
CREATE INDEX IF NOT EXISTS IX_files_Sha1Hash ON files (
    "Sha1Hash"
);


-- Index: IX_files_Sha256Hash_BoardId
CREATE UNIQUE INDEX IF NOT EXISTS IX_files_Sha256Hash_BoardId ON files (
    "Sha256Hash",
    "BoardId"
);


-- Index: IX_files_StreamHash
CREATE INDEX IF NOT EXISTS IX_files_StreamHash ON files (
    "StreamHash"
);


-- Index: IX_posts_BoardId_ThreadId_DateTime
CREATE INDEX IF NOT EXISTS IX_posts_BoardId_ThreadId_DateTime ON posts (
    "BoardId",
    "ThreadId",
    "DateTime"
);


-- Index: IX_threads_LastModified
CREATE INDEX IF NOT EXISTS IX_threads_LastModified ON threads (
    "LastModified"
);


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
