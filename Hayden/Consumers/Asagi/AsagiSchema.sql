CREATE TABLE IF NOT EXISTS `index_counters` (
  `id` varchar(50) NOT NULL,
  `val` int(10) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8; $

CREATE TABLE IF NOT EXISTS `{0}` (
  `doc_id` int unsigned NOT NULL auto_increment,
  `media_id` int unsigned NOT NULL DEFAULT '0',
  `poster_ip` decimal(39,0) unsigned NOT NULL DEFAULT '0',
  `num` int unsigned NOT NULL,
  `subnum` int unsigned NOT NULL,
  `thread_num` int unsigned NOT NULL DEFAULT '0',
  `op` bool NOT NULL DEFAULT '0',
  `timestamp` int unsigned NOT NULL,
  `timestamp_expired` int unsigned NOT NULL,
  `preview_orig` varchar(20),
  `preview_w` smallint unsigned NOT NULL DEFAULT '0',
  `preview_h` smallint unsigned NOT NULL DEFAULT '0',
  `media_filename` text,
  `media_w` smallint unsigned NOT NULL DEFAULT '0',
  `media_h` smallint unsigned NOT NULL DEFAULT '0',
  `media_size` int unsigned NOT NULL DEFAULT '0',
  `media_hash` varchar(25),
  `media_orig` varchar(20),
  `spoiler` bool NOT NULL DEFAULT '0',
  `deleted` bool NOT NULL DEFAULT '0',
  `capcode` varchar(1) NOT NULL DEFAULT 'N',
  `email` varchar(100),
  `name` varchar(100),
  `trip` varchar(25),
  `title` varchar(100),
  `comment` text,
  `delpass` tinytext,
  `sticky` bool NOT NULL DEFAULT '0',
  `locked` bool NOT NULL DEFAULT '0',
  `poster_hash` varchar(8),
  `poster_country` varchar(2),
  `exif` text,

  PRIMARY KEY (`doc_id`),
  UNIQUE num_subnum_index (`num`, `subnum`),
  INDEX thread_num_subnum_index (`thread_num`, `num`, `subnum`),
  INDEX subnum_index (`subnum`),
  INDEX op_index (`op`),
  INDEX media_id_index (`media_id`),
  INDEX media_hash_index (`media_hash`),
  INDEX media_orig_index (`media_orig`),
  INDEX name_trip_index (`name`, `trip`),
  INDEX trip_index (`trip`),
  INDEX email_index (`email`),
  INDEX poster_ip_index (`poster_ip`),
  INDEX timestamp_index (`timestamp`)
) engine=InnoDB CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_deleted` LIKE `{0}`;


CREATE TABLE IF NOT EXISTS `{0}_threads` (
  `thread_num` int unsigned NOT NULL,
  `time_op` int unsigned NOT NULL,
  `time_last` int unsigned NOT NULL,
  `time_bump` int unsigned NOT NULL,
  `time_ghost` int unsigned DEFAULT NULL,
  `time_ghost_bump` int unsigned DEFAULT NULL,
  `time_last_modified` int unsigned NOT NULL,
  `nreplies` int unsigned NOT NULL DEFAULT '0',
  `nimages` int unsigned NOT NULL DEFAULT '0',
  `sticky` bool NOT NULL DEFAULT '0',
  `locked` bool NOT NULL DEFAULT '0',

  PRIMARY KEY (`thread_num`),
  INDEX time_op_index (`time_op`),
  INDEX time_bump_index (`time_bump`),
  INDEX time_ghost_bump_index (`time_ghost_bump`),
  INDEX time_last_modified_index (`time_last_modified`),
  INDEX sticky_index (`sticky`),
  INDEX locked_index (`locked`)
) ENGINE=InnoDB CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_users` (
  `user_id` int unsigned NOT NULL auto_increment,
  `name` varchar(100) NOT NULL DEFAULT '',
  `trip` varchar(25) NOT NULL DEFAULT '',
  `firstseen` int(11) NOT NULL,
  `postcount` int(11) NOT NULL,

  PRIMARY KEY (`user_id`),
  UNIQUE name_trip_index (`name`, `trip`),
  INDEX firstseen_index (`firstseen`),
  INDEX postcount_index (`postcount`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_images` (
  `media_id` int unsigned NOT NULL auto_increment,
  `media_hash` varchar(25) NOT NULL,
  `media` varchar(20),
  `preview_op` varchar(20),
  `preview_reply` varchar(20),
  `total` int(10) unsigned NOT NULL DEFAULT '0',
  `banned` smallint unsigned NOT NULL DEFAULT '0',

  PRIMARY KEY (`media_id`),
  UNIQUE media_hash_index (`media_hash`),
  INDEX total_index (`total`),
  INDEX banned_index (`banned`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;


CREATE TABLE IF NOT EXISTS `{0}_daily` (
  `day` int(10) unsigned NOT NULL,
  `posts` int(10) unsigned NOT NULL,
  `images` int(10) unsigned NOT NULL,
  `sage` int(10) unsigned NOT NULL,
  `anons` int(10) unsigned NOT NULL,
  `trips` int(10) unsigned NOT NULL,
  `names` int(10) unsigned NOT NULL,

  PRIMARY KEY (`day`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

$

DROP PROCEDURE IF EXISTS `update_thread_{0}`; $


CREATE PROCEDURE `update_thread_{0}` (tnum INT, ghost_num INT, p_timestamp INT,
  p_media_hash VARCHAR(25), p_email VARCHAR(100))
BEGIN
  DECLARE d_time_last INT;
  DECLARE d_time_bump INT;
  DECLARE d_time_ghost INT;
  DECLARE d_time_ghost_bump INT;
  DECLARE d_time_last_modified INT;
  DECLARE d_image INT;

  SET d_time_last = 0;
  SET d_time_bump = 0;
  SET d_time_ghost = 0;
  SET d_time_ghost_bump = 0;
  SET d_image = p_media_hash IS NOT NULL;

  IF (ghost_num = 0) THEN
    SET d_time_last_modified = p_timestamp;
    SET d_time_last = p_timestamp;
    IF (p_email <> 'sage' OR p_email IS NULL) THEN
      SET d_time_bump = p_timestamp;
    END IF;
  ELSE
    SET d_time_last_modified = p_timestamp;
    SET d_time_ghost = p_timestamp;
    IF (p_email <> 'sage' OR p_email IS NULL) THEN
      SET d_time_ghost_bump = p_timestamp;
    END IF;
  END IF;

  UPDATE
    `{0}_threads` op
  SET
    op.time_last = (
      COALESCE(
        GREATEST(op.time_op, d_time_last),
        op.time_op
      )
    ),
    op.time_bump = (
      COALESCE(
        GREATEST(op.time_bump, d_time_bump),
        op.time_op
      )
    ),
    op.time_ghost = (
      IF (
        GREATEST(
          IFNULL(op.time_ghost, 0),
          d_time_ghost
        ) <> 0,
        GREATEST(
          IFNULL(op.time_ghost, 0),
          d_time_ghost
        ),
        NULL
      )
    ),
    op.time_ghost_bump = (
      IF(
        GREATEST(
          IFNULL(op.time_ghost_bump, 0),
          d_time_ghost_bump
        ) <> 0,
        GREATEST(
          IFNULL(op.time_ghost_bump, 0),
          d_time_ghost_bump
        ),
        NULL
      )
    ),
    op.time_last_modified = (
      COALESCE(
        GREATEST(op.time_last_modified, d_time_last_modified),
        op.time_op
      )
    ),
    op.nreplies = (
      op.nreplies + 1
    ),
    op.nimages = (
      op.nimages + d_image
    )
    WHERE op.thread_num = tnum;
END; $


DROP PROCEDURE IF EXISTS `update_thread_timestamp_{0}`; $


CREATE PROCEDURE `update_thread_timestamp_{0}` (tnum INT, timestamp INT)
BEGIN
  UPDATE
    `{0}_threads` op
  SET
    op.time_last_modified = (
      GREATEST(op.time_last_modified, timestamp)
    )
  WHERE op.thread_num = tnum;
END; $


DROP PROCEDURE IF EXISTS `create_thread_{0}`; $


CREATE PROCEDURE `create_thread_{0}` (num INT, timestamp INT)
BEGIN
  INSERT IGNORE INTO `{0}_threads` VALUES (num, timestamp, timestamp,
    timestamp, NULL, NULL, timestamp, 0, 0, 0, 0);
END; $


DROP PROCEDURE IF EXISTS `delete_thread_{0}`; $


CREATE PROCEDURE `delete_thread_{0}` (tnum INT)
BEGIN
  DELETE FROM `{0}_threads` WHERE thread_num = tnum;
END; $


DROP PROCEDURE IF EXISTS `insert_image_{0}`; $


CREATE PROCEDURE `insert_image_{0}` (n_media_hash VARCHAR(25),
 n_media VARCHAR(20), n_preview VARCHAR(20), n_op INT)
BEGIN
  IF n_op = 1 THEN
    INSERT INTO `{0}_images` (media_hash, media, preview_op, total)
    VALUES (n_media_hash, n_media, n_preview, 1)
    ON DUPLICATE KEY UPDATE
      media_id = LAST_INSERT_ID(media_id),
      total = (total + 1),
      preview_op = COALESCE(preview_op, VALUES(preview_op)),
      media = COALESCE(media, VALUES(media));
  ELSE
    INSERT INTO `{0}_images` (media_hash, media, preview_reply, total)
    VALUES (n_media_hash, n_media, n_preview, 1)
    ON DUPLICATE KEY UPDATE
      media_id = LAST_INSERT_ID(media_id),
      total = (total + 1),
      preview_reply = COALESCE(preview_reply, VALUES(preview_reply)),
      media = COALESCE(media, VALUES(media));
  END IF;
END; $


DROP PROCEDURE IF EXISTS `delete_image_{0}`; $


CREATE PROCEDURE `delete_image_{0}` (n_media_id INT)
BEGIN
  UPDATE `{0}_images` SET total = (total - 1) WHERE media_id = n_media_id;
END; $


DROP TRIGGER IF EXISTS `before_ins_{0}`; $


CREATE TRIGGER `before_ins_{0}` BEFORE INSERT ON `{0}`
FOR EACH ROW
BEGIN
  IF NEW.media_hash IS NOT NULL THEN
    CALL insert_image_{0}(NEW.media_hash, NEW.media_orig, NEW.preview_orig, NEW.op);
    SET NEW.media_id = LAST_INSERT_ID();
  END IF;
END; $


DROP TRIGGER IF EXISTS `after_ins_{0}`; $


CREATE TRIGGER `after_ins_{0}` AFTER INSERT ON `{0}`
FOR EACH ROW
BEGIN
  IF NEW.op = 1 THEN
    CALL create_thread_{0}(NEW.num, NEW.timestamp);
  END IF;
  CALL update_thread_{0}(NEW.thread_num, NEW.subnum, NEW.timestamp, NEW.media_hash, NEW.email);
END; $


DROP TRIGGER IF EXISTS `after_del_{0}`; $


CREATE TRIGGER `after_del_{0}` AFTER DELETE ON `{0}`
FOR EACH ROW
BEGIN
  CALL update_thread_{0}(OLD.thread_num, OLD.subnum, OLD.timestamp, OLD.media_hash, OLD.email);
  IF OLD.op = 1 THEN
    CALL delete_thread_{0}(OLD.num);
  END IF;
  IF OLD.media_hash IS NOT NULL THEN
    CALL delete_image_{0}(OLD.media_id);
  END IF;
END; $


CREATE TRIGGER `after_upd_{0}` AFTER UPDATE ON `{0}`
FOR EACH ROW
BEGIN
  IF NEW.timestamp_expired <> 0 THEN
    CALL update_thread_timestamp_{0}(NEW.thread_num, NEW.timestamp_expired);
  END IF;
END;